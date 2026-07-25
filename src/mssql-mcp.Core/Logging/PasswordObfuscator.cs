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
    // followed by one of three value forms. Each branch must consume the FULL value,
    // including any trailing non-`;` chars, so no tail of the value leaks verbatim.
    //   1. Quoted:   Password="..."[^;]*  — quoted value (may contain "" escapes and ;)
    //                 followed by any trailing non-`;` chars before the delimiter
    //   2. Braced:   Password={...}[^;]*  — braced value (may contain ;) followed by
    //                 any trailing non-`;` chars
    //   3. Plain:    Password=[^;]*  — up to the next `;` or end of string
    // Branches (1) and (2) consume the closing delimiter AND any trailing junk so a
    // malformed input like `Password="x"y";tail=4;` matches the whole `Password="x"y"`
    // segment instead of stopping at `"x"` and leaking `y"`. AccessToken and Token carry
    // Azure AD credentials and must be obfuscated too.
    [GeneratedRegex(
        @"(?:Password|PWD|AccessToken|Token)=(""(?:[^""]|"""")*""[^;]*|\{[^}]*\}[^;]*|[^;]*);?",
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
            // Defensive: if a pathological input ever exceeds the regex timeout, return a
            // fixed redaction string instead of the raw input — never leak credentials.
            return "[redacted: regex timeout]";
        }
    }
}
