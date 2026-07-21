namespace mssql_mcp.Core.Guard;

/// <summary>
/// Result of Guard validation. On accept, <see cref="WrappedSql"/> is ready to execute
/// (sentinel + transaction wrapper applied per ADR-0007). On reject, <see cref="Rejection"/>
/// carries the structured rule name and position per ADR-0010.
/// </summary>
public sealed record GuardResult
{
    /// <summary>True when the SQL passed both AST layers and is safe for Restricted execution.</summary>
    public bool Accepted { get; init; }

    /// <summary>SQL wrapped with sentinel + transaction pair, populated when <see cref="Accepted"/> is true.</summary>
    public string? WrappedSql { get; init; }

    /// <summary>Structured rejection detail, populated when <see cref="Accepted"/> is false.</summary>
    public GuardRejection? Rejection { get; init; }

    public static GuardResult Accept(string wrappedSql) => new() { Accepted = true, WrappedSql = wrappedSql };
    public static GuardResult Reject(GuardRejection rejection) => new() { Accepted = false, Rejection = rejection };
}

/// <summary>
/// Structured Guard rejection per ADR-0010 GUARD_REJECTION shape.
/// <c>Rule</c> names the specific rule violated so agents can self-correct.
/// </summary>
public sealed record GuardRejection
{
    /// <summary>Rule discriminator: parse_error, empty_batch, non_select_statement, statement_snippet,
    /// select_into, openrowset, openquery, openxml, opendatasource, execute_as, four_part_name, bulk_insert.</summary>
    public string Rule { get; init; }

    /// <summary>Human-readable detail suitable for the GUARD_REJECTION detail field.</summary>
    public string Detail { get; init; }

    /// <summary>Concrete T-SQL statement type that triggered the rejection, when applicable.</summary>
    public string? StatementType { get; init; }

    /// <summary>1-based line of the rejecting token, when known.</summary>
    public int? Line { get; init; }

    /// <summary>1-based column of the rejecting token, when known.</summary>
    public int? Column { get; init; }

    public GuardRejection(string rule, string detail, string? statementType = null, int? line = null, int? column = null)
    {
        Rule = rule;
        Detail = detail;
        StatementType = statementType;
        Line = line;
        Column = column;
    }
}

/// <summary>
/// Validates a SQL string against the Restricted-mode allowlist (ADR-0006).
/// In Unrestricted mode, validation is skipped — the caller gets an accept result
/// without the transaction wrapper (Unrestricted writes are allowed).
/// </summary>
public interface IGuard
{
    /// <summary>
    /// Validates the SQL and returns either an accept result (with wrapped SQL ready
    /// for execution) or a structured rejection.
    /// </summary>
    GuardResult Validate(string sql);
}
