using mssql_mcp.Core.Logging;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for <see cref="PasswordObfuscator"/> — verifies the regex covers every form of
/// <c>Password=...;</c> that can appear in a SQL Server connection string fragment logged by
/// the server. Per ADR-0005 (password obfuscation) and ADR-0011 (logging).
/// </summary>
public class PasswordObfuscationTests
{
    [Fact]
    public void Obfuscate_SimplePassword()
    {
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("Password=secret123;"));
    }

    [Fact]
    public void Obfuscate_EmptyPassword()
    {
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("Password=;"));
    }

    [Fact]
    public void Obfuscate_SpecialChars()
    {
        // Passwords containing non-semicolon special characters (@, !, digits, $) are
        // treated as part of the value and fully replaced.
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("Password=p@ssw0rd!;"));
    }

    [Fact]
    public void Obfuscate_MidString()
    {
        // Connection string fragment with the password in the middle.
        Assert.Equal(
            "User Id=sa;Password=***;Database=master;",
            PasswordObfuscator.Obfuscate("User Id=sa;Password=secret;Database=master;"));
    }

    [Fact]
    public void Obfuscate_NoPassword()
    {
        // Connection strings without a password are returned unchanged.
        Assert.Equal(
            "Server=localhost;Database=master;",
            PasswordObfuscator.Obfuscate("Server=localhost;Database=master;"));
    }

    [Fact]
    public void Obfuscate_MultiplePasswords()
    {
        // Two occurrences in one message — both replaced.
        string input = "Old=Password=abc123; New=Password=xyz789;";
        string expected = "Old=Password=***; New=Password=***;";
        Assert.Equal(expected, PasswordObfuscator.Obfuscate(input));
    }

    [Fact]
    public void Obfuscate_CaseInsensitive()
    {
        // Connection string keys are case-insensitive in SqlClient — the regex must match any
        // case variation of "Password=". Replacement normalizes to canonical "Password=***;".
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("PASSWORD=secret;"));
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("password=secret;"));
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("PaSsWoRd=secret;"));
    }

    [Fact]
    public void Obfuscate_QuotedPasswordWithSemicolon()
    {
        // SQL Server allows Password="my;pass"; where the value contains a semicolon.
        // The regex must consume the entire quoted value, not stop at the first ;.
        string input = "Server=localhost;Password=\"my;pass\";Database=master;";
        string expected = "Server=localhost;Password=***;Database=master;";
        Assert.Equal(expected, PasswordObfuscator.Obfuscate(input));
    }

    [Fact]
    public void Obfuscate_BracedPasswordWithSemicolon()
    {
        // SQL Server allows Password={my;pass}; where the value contains a semicolon.
        string input = "Server=localhost;Password={my;pass};Database=master;";
        string expected = "Server=localhost;Password=***;Database=master;";
        Assert.Equal(expected, PasswordObfuscator.Obfuscate(input));
    }

    [Fact]
    public void Obfuscate_NoTrailingSemicolon()
    {
        // Truncated log lines may end without a semicolon. ADR-0005 requires obfuscation
        // in ALL log output, including unterminated fragments.
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("Password=secret"));
    }

    [Fact]
    public void Obfuscate_PwdAlias_Obfuscated()
    {
        // Microsoft.Data.SqlClient accepts both Password= and PWD= as the password key.
        // Both must be obfuscated to prevent cleartext leakage.
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("PWD=secret;"));
        Assert.Equal("Password=***;", PasswordObfuscator.Obfuscate("pwd=secret;"));
    }

    [Fact]
    public void Obfuscate_EmptyMessage_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PasswordObfuscator.Obfuscate(string.Empty));
    }
}
