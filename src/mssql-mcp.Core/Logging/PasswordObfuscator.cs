using System.Text.RegularExpressions;

namespace mssql_mcp.Core.Logging;

/// <summary>
/// Regex-based obfuscation of SQL Server connection-string passwords in log messages.
/// Replaces <c>Password=...;</c> with <c>Password=***;</c> per ADR-0005 and ADR-0011.
/// Applied to every log entry on every sink (stderr, file) before the formatter sees it.
/// </summary>
public static partial class PasswordObfuscator
{
    // Match <c>Password=</c> or <c>PWD=</c> (case-insensitive) followed by one of three value forms:
    //   1. Quoted:   <c>Password="...";</c> — value may contain <c>;</c>, <c>""</c> is escaped quote
    //   2. Braced:   <c>Password={...};</c> — value may contain <c>;</c>, no <c>}</c> allowed inside
    //   3. Plain:    <c>Password=...;</c> or <c>Password=...</c> (unterminated at end of string)
    // The trailing <c>;</c> is optional so unterminated fragments (e.g. truncated log lines) are
    // still obfuscated per ADR-0005's "in all log output" contract.
    // <c>PWD=</c> is accepted as an alias — Microsoft.Data.SqlClient treats both as the password key.
    [GeneratedRegex(
        @"(?:Password|PWD)=(""(?:[^""]|"""")*""|\{[^}]*\}|[^;{}]*);?",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PasswordPattern { get; }

    public const string Replacement = "Password=***;";

    /// <summary>
    /// Returns <paramref name="message"/> with every <c>Password=...;</c> occurrence replaced by
    /// <c>Password=***;</c>. Returns the original message if no password segment is present.
    /// </summary>
    public static string Obfuscate(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        try
        {
            return PasswordPattern.Replace(message, Replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            // Defensive: if a pathological input ever exceeds the regex timeout, return the
            // original rather than dropping the log line. The caller still gets a log entry.
            return message;
        }
    }
}
