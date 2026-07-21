using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Tools;

/// <summary>
/// Execution-plan tools (the <c>explain_query</c> surface). Uses <c>SET SHOWPLAN_XML ON</c>
/// to obtain the estimated plan without executing the query. The Guard validates the SQL
/// in BOTH Restricted and Unrestricted modes per ADR-0016 — there is no legitimate reason
/// to bypass plan analysis even in Unrestricted mode.
/// </summary>
[McpServerToolType]
public sealed class PlanTools
{
    private const string ShowPlanNamespace = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    private const int TopOperationsLimit = 5;

    private static readonly XNamespace Ns = ShowPlanNamespace;

    private readonly ISqlExecutor _executor;
    private readonly IGuard _guard;
    private readonly MssqlMcpOptions _options;
    private readonly ILogger<PlanTools> _logger;

    public PlanTools(ISqlExecutor executor, IGuard guard, IOptions<MssqlMcpOptions> options, ILogger<PlanTools> logger)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _executor = executor;
        _guard = guard;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the execution plan for a T-SQL query without executing it. Summary format
    /// extracts estimated cost, missing indexes, warnings, and top 5 operations. XML format
    /// returns the raw SHOWPLAN_XML. Guard validation runs in BOTH modes (no bypass).
    /// </summary>
    [McpServerTool(Name = "explain_query", Title = "Explain query", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Returns the execution plan for a T-SQL query without executing it. Summary format extracts estimated cost, missing indexes, warnings, and top 5 operations. XML format returns raw SHOWPLAN_XML. Guard validates in both Restricted and Unrestricted modes.")]
    public async Task<CallToolResult> ExplainQuery(
        [Description("The T-SQL query to get an execution plan for.")] string sql,
        [Description("Output format: 'summary' (default) or 'xml'.")] string? format,
        CancellationToken ct)
    {
        _logger.LogInformation("[tool] explain_query invoked (format={Format})", format ?? "<summary>");

        // ValidateStrict always runs the AST allowlist (ADR-0006) regardless of AccessMode —
        // explain_query does not bypass even in Unrestricted mode.
        GuardResult guardResult = _guard.ValidateStrict(sql);
        if (!guardResult.Accepted)
        {
            GuardRejection rejection = guardResult.Rejection
                ?? new GuardRejection("non_select_statement", "[guard] Rejected with no reason.");
            return GuardRejectionError(rejection);
        }
        if (guardResult.WrappedSql is null)
        {
            return ToolErrors.Internal(new InvalidOperationException("Guard accepted but WrappedSql was null."));
        }

        string planXml;
        try
        {
            planXml = await _executor.ExecuteShowPlanXmlAsync(guardResult.WrappedSql, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[timeout] explain_query exceeded {Timeout}s command timeout", _options.QueryTimeout);
            return ToolErrors.Timeout(_options.QueryTimeout);
        }
        catch (SqlException ex)
        {
            _logger.LogError("[sql] explain_query failed: {Message} (code {Number}, severity {Severity})", ex.Message, ex.Number, ex.Class);
            return ToolErrors.SqlError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[internal] explain_query unhandled exception: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return ToolErrors.Internal(ex);
        }

        // format: "xml" returns raw SHOWPLAN_XML; anything else (null, "summary", unknown) → summary.
        if (string.Equals(format, "xml", StringComparison.OrdinalIgnoreCase))
        {
            return ToolErrors.Success(planXml);
        }

        object summary = BuildSummary(planXml);
        string json = JsonSerializer.Serialize(summary, ToolErrors.JsonOptions);
        _logger.LogInformation("[tool] explain_query returned summary");
        return ToolErrors.Success(json);
    }

    /// <summary>
    /// Parses SHOWPLAN_XML and extracts: estimated total cost, missing indexes, warnings,
    /// and top 5 RelOp nodes sorted by estimated cost (CPU + IO) descending. Per ADR-0016.
    /// </summary>
    private static object BuildSummary(string planXml)
    {
        XDocument doc = XDocument.Parse(planXml);

        // StmtSimple carries StatementSubTreeCost — use it as the canonical total cost.
        double totalCost = doc.Descendants(Ns + "StmtSimple")
            .Select(s => TryParseDouble(s.Attribute("StatementSubTreeCost")?.Value))
            .Where(v => v.HasValue)
            .Sum(v => v.GetValueOrDefault());

        List<object> missingIndexes = doc.Descendants(Ns + "MissingIndex")
            .Select(BuildMissingIndex)
            .ToList();

        List<string> warnings = doc.Descendants(Ns + "Warnings")
            .SelectMany(w => w.Attributes())
            .Where(a => string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase))
            .Select(a => ToWarningName(a.Name.LocalName))
            .ToList();

        // Each RelOp carries EstimateCPU + EstimateIO. Cost = CPU + IO. Descending, take top 5.
        List<RelOpSummary> topOps = doc.Descendants(Ns + "RelOp")
            .Select(BuildRelOpSummary)
            .OrderByDescending(r => r.EstimatedCost)
            .Take(TopOperationsLimit)
            .ToList();

        return new
        {
            estimated_total_cost = Math.Round(totalCost, 4, MidpointRounding.ToEven),
            missing_indexes = missingIndexes,
            warnings,
            top_operations = topOps.Select(r => (object)new
            {
                operation = r.Operation,
                estimated_cost = r.EstimatedCost,
                estimated_rows = r.EstimatedRows,
                @object = r.Object,
            }).ToList(),
        };
    }

    private static object BuildMissingIndex(XElement mi)
    {
        // MissingIndex element shape:
        //   <MissingIndexes>
        //     <MissingIndexGroup Impact="95.0">
        //       <MissingIndex Database="[AppDb]" Schema="[dbo]" Table="[Orders]">...
        // Impact lives on MissingIndexGroup (the parent of MissingIndex).
        XElement? groupEl = mi.Parent;
        string? impact = groupEl?.Attribute("Impact")?.Value;
        double? impactValue = TryParseDouble(impact);

        // Column groups: EQUALITY / INEQUALITY / INCLUDE — collect column names per group.
        List<string> eqCols = new();
        List<string> ineqCols = new();
        foreach (XElement cg in mi.Elements(Ns + "ColumnGroup"))
        {
            string? usage = cg.Attribute("Usage")?.Value;
            List<string> target = string.Equals(usage, "EQUALITY", StringComparison.OrdinalIgnoreCase) ? eqCols
                : string.Equals(usage, "INEQUALITY", StringComparison.OrdinalIgnoreCase) ? ineqCols
                : new List<string>();
            foreach (XElement col in cg.Elements(Ns + "Column"))
            {
                string? name = col.Attribute("Name")?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    target.Add(name);
                }
            }
        }

        return new
        {
            impact = impactValue ?? 0.0,
            database = StripBrackets(mi.Attribute("Database")?.Value),
            schema = StripBrackets(mi.Attribute("Schema")?.Value),
            table = StripBrackets(mi.Attribute("Table")?.Value),
            equality_columns = eqCols.Count > 0 ? string.Join(", ", eqCols) : null,
            inequality_columns = ineqCols.Count > 0 ? string.Join(", ", ineqCols) : null,
        };
    }

    private static RelOpSummary BuildRelOpSummary(XElement relOp)
    {
        double cpu = TryParseDouble(relOp.Attribute("EstimateCPU")?.Value) ?? 0.0;
        double io = TryParseDouble(relOp.Attribute("EstimateIO")?.Value) ?? 0.0;
        double rows = TryParseDouble(relOp.Attribute("EstimateRows")?.Value) ?? 0.0;

        string? physicalOp = relOp.Attribute("PhysicalOp")?.Value;
        XElement? objectEl = relOp.Element(Ns + "Object");
        string? objectName = objectEl is null ? null : string.Join(".",
            new[] { objectEl.Attribute("Database")?.Value, objectEl.Attribute("Schema")?.Value,
                    objectEl.Attribute("Table")?.Value, objectEl.Attribute("Index")?.Value }
                .Where(s => !string.IsNullOrEmpty(s)));

        return new RelOpSummary
        {
            Operation = physicalOp ?? string.Empty,
            EstimatedCost = Math.Round(cpu + io, 4, MidpointRounding.ToEven),
            EstimatedRows = Math.Round(rows, 0, MidpointRounding.ToEven),
            Object = objectName,
        };
    }

    private static double? TryParseDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
        {
            return v;
        }
        return null;
    }

    private static string StripBrackets(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }
        return s.Trim('[', ']');
    }

    /// <summary>
    /// Converts an XML attribute name like <c>NoJoinPredicate</c> to the canonical warning
    /// name <c>NO_JOIN_PREDICATE</c> (UPPER_SNAKE_CASE).
    /// </summary>
    private static string ToWarningName(string localName)
    {
        if (string.IsNullOrEmpty(localName))
        {
            return string.Empty;
        }
        StringBuilder sb = new(localName.Length + 4);
        foreach (char c in localName)
        {
            if (char.IsUpper(c) && sb.Length > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private static CallToolResult GuardRejectionError(GuardRejection rejection)
    {
        object payload = new
        {
            error = "GUARD_REJECTION",
            rule = rejection.Rule,
            detail = rejection.Detail,
            statement_type = rejection.StatementType,
            position = rejection.Line is null && rejection.Column is null
                ? null
                : new { line = rejection.Line, column = rejection.Column },
        };
        string json = JsonSerializer.Serialize(payload, ToolErrors.JsonOptions);
        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = json } },
            IsError = true,
        };
    }

    private sealed record RelOpSummary
    {
        public string Operation { get; init; } = string.Empty;
        public double EstimatedCost { get; init; }
        public double EstimatedRows { get; init; }
        public string? Object { get; init; }
    }
}
