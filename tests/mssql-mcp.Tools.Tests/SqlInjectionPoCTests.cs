using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using NSubstitute;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// PoC for security-research candidate #1 (SQL injection via the `database` tool argument).
/// Disproves the hypothesis: the `database` parameter never reaches SqlConnection.ConnectionString,
/// and when it is used (list_schemas/list_objects/get_object_details) it is (1) validated against
/// sys.databases via a parameterized query, then (2) passed through SqlHelpers.QuoteIdentifier
/// which doubles internal `]` before wrapping in `[...]`, neutralizing bracket-escape injection.
/// `list_databases` itself takes no `database` parameter at all.
/// </summary>
public class SqlInjectionPoCTests
{
    // Crafted inputs covering classic bracket-escape and stacked-statement attempts.
    private static readonly string[] MaliciousDatabaseInputs =
    [
        "x]; DROP TABLE y; --",
        "db]; EXEC sp_executesql N'SELECT 1'; --",
        "db]; INSERT INTO sys.syslogins VALUES ('evil'); --",
        "name' OR 1=1; --",
        "db]']; WAITFOR DELAY '00:00:05'; --",
        "master]; SHUTDOWN WITH NOWAIT; --",
    ];

    private static DatabaseTools CreateTools(ISqlExecutor executor)
    {
        MssqlMcpOptions opts = new()
        {
            ConnectionString = "Server=localhost;Database=AppDb;",
            AccessMode = AccessMode.Restricted,
            QueryTimeout = 30,
            LogLevel = "info",
            MaxResultBytes = 10 * 1024 * 1024,
            RetryCount = 3,
            RetryIntervalMin = 2,
            RetryIntervalMax = 10,
        };
        return new DatabaseTools(executor, Options.Create(opts), NullLogger<DatabaseTools>.Instance);
    }

    private static List<Dictionary<string, object?>> ValidDbRow() =>
        [new() { ["state_desc"] = "ONLINE", ["user_access_desc"] = "MULTI_USER" }];

    private static List<Dictionary<string, object?>> EmptyRows() => [];

    private static string GetJson(CallToolResult r)
    {
        Assert.NotNull(r.Content);
        Assert.True(r.Content.Count >= 1);
        TextContentBlock block = Assert.IsType<TextContentBlock>(r.Content[0]);
        Assert.NotNull(block.Text);
        return block.Text;
    }

    // ---------- (1) list_databases takes NO database parameter ----------
    [Fact]
    public async Task ListDatabases_AcceptsNoDatabaseArgument_NoInjectionSurface()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();

        // Capture the SQL sent to ExecuteQueryAsync (non-parameterized overload).
        string capturedSql = string.Empty;
        executor.ExecuteQueryAsync(Arg.Do<string>(s => capturedSql = s ?? string.Empty), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>> { new() { ["name"] = "AppDb", ["database_id"] = 5L, ["state_desc"] = "ONLINE", ["is_current"] = true } });

        DatabaseTools tools = CreateTools(executor);
        // ListDatabases signature is (CancellationToken) — no database param exists.
        CallToolResult result = await tools.ListDatabases(CancellationToken.None);

        Assert.False(result.IsError ?? false);
        Assert.DoesNotContain("DROP", capturedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("EXEC", capturedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("SHUTDOWN", capturedSql, StringComparison.Ordinal);
    }

    // ---------- (2) For tools that DO take `database`, validation rejects unknown names ----------
    [Theory]
    [MemberData(nameof(MaliciousInputs))]
    public async Task ListSchemas_MaliciousDatabase_RejectedByValidation(string malicious)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // Validation query returns zero rows (no DB named "x]; DROP TABLE y; --" exists).
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(EmptyRows());

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListSchemas(database: malicious, CancellationToken.None);

        // Validation rejects → tool returns a ConnectionError, never executes the schema query.
        Assert.True(result.IsError ?? false);
        string json = GetJson(result);
        Assert.Contains("does not exist", json, StringComparison.Ordinal);

        // CRITICAL: the parameterized-overload (used for validation) was called exactly once,
        // and the NON-parameterized overload (used for the actual schema query) was NEVER called.
        await executor.Received(1).ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecuteQueryAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ---------- (3) Even IF validation somehow passed, QuoteIdentifier neutralizes the input ----------
    [Theory]
    [MemberData(nameof(MaliciousInputs))]
    public async Task ListSchemas_EvenIfValidationPassed_QuoteIdentifierNeutralizes(string malicious)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        // Validation succeeds (pretend the malicious name is a "valid" DB).
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValidDbRow());

        List<Dictionary<string, object?>> schemas =
        [
            new() { ["name"] = "dbo", ["schema_id"] = 1L },
        ];
        // Capture the SQL that actually gets executed.
        string capturedSql = string.Empty;
        executor.ExecuteQueryAsync(Arg.Do<string>(s => capturedSql = s ?? string.Empty), Arg.Any<CancellationToken>())
            .Returns(schemas);

        DatabaseTools tools = CreateTools(executor);
        CallToolResult result = await tools.ListSchemas(database: malicious, CancellationToken.None);

        Assert.False(result.IsError ?? false);

        // QuoteIdentifier doubles every `]` and wraps in `[...]`, so the malicious text is
        // absorbed as a SINGLE bracketed identifier — the brackets never close early, meaning
        // no `;`, `--`, or stacked statement can escape the identifier context.
        string quoted = "[" + malicious.Replace("]", "]]") + "]";
        Assert.Contains(quoted + ".sys.schemas", capturedSql, StringComparison.Ordinal);

        // Structural check: only one statement. The FROM clause must contain exactly one
        // `[<name>].sys.schemas` token. Verify no unescaped `]` (single `]` not followed by `]`)
        // appears after the opening `[` of the identifier — that would be the injection vector.
        int identStart = capturedSql.IndexOf('[');
        Assert.True(identStart >= 0);
        int identEnd = capturedSql.IndexOf("].", identStart, StringComparison.Ordinal);
        Assert.True(identEnd > identStart);
        string identContent = capturedSql.Substring(identStart, identEnd - identStart + 1);
        // Every `]` inside the identifier MUST be doubled — no lone `]` that closes the bracket early.
        for (int i = 1; i < identContent.Length - 1; i++)
        {
            if (identContent[i] == ']')
            {
                Assert.True(i + 1 < identContent.Length && identContent[i + 1] == ']',
                    $"Unescaped ']' at position {i} in identifier '{identContent}' — injection possible.");
                i++;
            }
        }
    }

    public static TheoryData<string> MaliciousInputs => new(MaliciousDatabaseInputs);
}
