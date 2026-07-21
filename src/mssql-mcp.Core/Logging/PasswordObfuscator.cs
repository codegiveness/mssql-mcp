using System.Text.RegularExpressions;

namespace mssql_mcp.Core.Logging;

/// <summary>
/// Regex-based obfuscation of SQL Server connection-string passwords in log messages.
/// Replaces <c>Password=...;</c> with <c>Password=***;</c> per ADR-0005 and ADR-0011.
/// Applied to every log entry on every sink (stderr, file) before the formatter sees it.
/// </summary>
public static partial class PasswordObfuscator
{
    // Match connection-string secrets: Password/PWD, AccessToken, or Token (case-insensitive)
    // followed by one of three value forms:
    //   1. Quoted:   Password="...";  — value may contain ;, "" is escaped quote
    //   2. Braced:   Password={...};  — value may contain ;, no } allowed inside
    //   3. Plain:    Password=...;  or Password=...  (unterminated at end of string)
    // AccessToken and Token carry Azure AD credentials and must be obfuscated too.
    [GeneratedRegex(
        @"(?:Password|PWD|AccessToken|Token)=(""(?:[^""]|"""")*""|\{[^}]*\}|[^;{}]*);?",
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
