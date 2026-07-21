using NSubstitute;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for <see cref="SqlHelpers.QuoteIdentifier"/> (SQL-injection load-bearing) and
/// <see cref="SqlHelpers.ValidateDatabaseAsync"/> (cross-DB safety per ADR-0016).
/// </summary>
public class SqlHelpersTests
{
    // ---------- QuoteIdentifier ----------

    [Theory]
    [InlineData("normal", "[normal]")]
    [InlineData("dbo", "[dbo]")]
    [InlineData("MyDatabase", "[MyDatabase]")]
    public void QuoteIdentifier_NormalName_ReturnsBracketed(string input, string expected)
    {
        Assert.Equal(expected, SqlHelpers.QuoteIdentifier(input));
    }

    [Fact]
    public void QuoteIdentifier_NameWithBracket_ReturnsDoubled()
    {
        Assert.Equal("[my]]db]", SqlHelpers.QuoteIdentifier("my]db"));
    }

    [Fact]
    public void QuoteIdentifier_NameWithMultipleBrackets_ReturnsAllDoubled()
    {
        Assert.Equal("[my]]weird]]db]]]", SqlHelpers.QuoteIdentifier("my]weird]db]"));
    }

    [Fact]
    public void QuoteIdentifier_EmptyString_ReturnsEmptyBrackets()
    {
        Assert.Equal("[]", SqlHelpers.QuoteIdentifier(string.Empty));
    }

    [Fact]
    public void QuoteIdentifier_InjectionAttemptIsNeutralized()
    {
        // Classic injection: name = "x]; DROP TABLE y; --"
        // Without doubling, "[x]; DROP TABLE y; --]" would be interpreted as the identifier "x"
        // followed by an out-of-band statement. With doubling, the ] is escaped and the whole
        // thing becomes a single (admittedly weird) identifier.
        string result = SqlHelpers.QuoteIdentifier("x]; DROP TABLE y; --");
        Assert.Equal("[x]]; DROP TABLE y; --]", result);
        // The result is a single bracketed identifier — no run-on statement possible.
        Assert.StartsWith("[", result);
        Assert.EndsWith("]", result);
    }

    // ---------- ValidateDatabaseAsync ----------

    private static List<Dictionary<string, object?>> DbRow(string stateDesc, string userAccessDesc) =>
    [
        new()
        {
            ["state_desc"] = stateDesc,
            ["user_access_desc"] = userAccessDesc,
        },
    ];

    [Fact]
    public async Task ValidateDatabase_ExistsOnlineMultiUser_ReturnsValid()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(DbRow("ONLINE", "MULTI_USER"));

        DatabaseValidationResult result = await SqlHelpers.ValidateDatabaseAsync(executor, "AppDb", CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ValidateDatabase_NoRows_ReturnsDoesNotExistError()
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object?>>());

        DatabaseValidationResult result = await SqlHelpers.ValidateDatabaseAsync(executor, "MissingDb", CancellationToken.None);

        Assert.False(result.Valid);
        Assert.NotNull(result.Error);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
        Assert.Contains("MissingDb", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RESTORING")]
    [InlineData("OFFLINE")]
    [InlineData("SUSPECT")]
    [InlineData("EMERGENCY")]
    public async Task ValidateDatabase_NotOnline_ReturnsNotOnlineError(string stateDesc)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(DbRow(stateDesc, "MULTI_USER"));

        DatabaseValidationResult result = await SqlHelpers.ValidateDatabaseAsync(executor, "AppDb", CancellationToken.None);

        Assert.False(result.Valid);
        Assert.NotNull(result.Error);
        Assert.Contains("not online", result.Error, StringComparison.Ordinal);
        Assert.Contains(stateDesc, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SINGLE_USER")]
    [InlineData("RESTRICTED_USER")]
    public async Task ValidateDatabase_NotMultiUser_ReturnsNotMultiUserError(string userAccessDesc)
    {
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(DbRow("ONLINE", userAccessDesc));

        DatabaseValidationResult result = await SqlHelpers.ValidateDatabaseAsync(executor, "AppDb", CancellationToken.None);

        Assert.False(result.Valid);
        Assert.NotNull(result.Error);
        Assert.Contains("not multi-user", result.Error, StringComparison.Ordinal);
        Assert.Contains(userAccessDesc, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateDatabase_PassesDatabaseNameAsParameter_NotInline()
    {
        IReadOnlyDictionary<string, object>? captured = null;
        ISqlExecutor executor = Substitute.For<ISqlExecutor>();
        executor.ExecuteQueryAsync(
                Arg.Any<string>(),
                Arg.Do<IReadOnlyDictionary<string, object>?>(p => captured = p),
                Arg.Any<CancellationToken>())
            .Returns(DbRow("ONLINE", "MULTI_USER"));

        await SqlHelpers.ValidateDatabaseAsync(executor, "AppDb", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.True(captured!.TryGetValue("database", out object? v));
        Assert.Equal("AppDb", v);
    }
}
