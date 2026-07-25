using mssql_mcp.Core.Logging;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// PoC #2 — PasswordObfuscator partial-obfuscation leak (revised finding).
///
/// Original candidate hypothesis (regex timeout returns raw input) is FALSIFIED:
/// probe with inputs up to 100K chars completes in 0-4ms, never hits the 200ms timeout.
/// The catch block at PasswordObfuscator.cs:42-46 is dead code for any realistic input.
///
/// Real finding: the regex alternation `(?:"(?:[^"]|"")*"|\{[^}]*\}|[^;{}]*)` can match
/// a SHORTER prefix of the password value and leave the tail UNOBFUSCATED in the output.
/// When the input contains a `Password="..."` segment where the quoted branch matches an
/// early close (e.g. an embedded `""` escape pair, or a quoted value followed by more
/// text that looks like another key-value), the regex replaces only the matched prefix
/// and the remainder — which still contains password characters — is emitted verbatim.
///
/// This is a CWE-209 (info exposure via error/log output) / CWE-532 (credential in log)
/// gap, but only under crafted/edge-case inputs. Normal connection strings obfuscate
/// correctly (control test below).
/// </summary>
public class PocObfuscationPartialLeakTests
{
    /// <summary>
    /// FALSIFICATION of the original timeout hypothesis: even a 100K-char pathological
    /// input completes within the 200ms timeout. The catch block is unreachable for
    /// realistic inputs. (If a future runtime regression makes this flip, the test
    /// surfaces it.)
    /// </summary>
    [Fact]
    public void PA1_RegexTimeoutHypothesis_Falsified_NoTimeoutOnLargeInput()
    {
        // 100K-char mixed-alphabet input that exercises all three alternation branches.
        var sb = new System.Text.StringBuilder(100_040);
        sb.Append("Password=");
        var rng = new Random(42);
        string alphabet = "\"{};x";
        for (int i = 0; i < 100_000; i++) sb.Append(alphabet[rng.Next(alphabet.Length)]);
        sb.Append("REAL_SECRET;tail=1");
        string input = sb.ToString();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string result = PasswordObfuscator.Obfuscate(input);
        sw.Stop();

        // The timeout never fires — the regex completes well under 200ms.
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"regex completed in {sw.ElapsedMilliseconds}ms, timeout did not fire");
        // Obfuscation ran (output differs from input).
        Assert.NotEqual(input, result);
    }

    /// <summary>
    /// INVERTED: the quoted-value branch now consumes the closing `"` AND any trailing
    /// non-`;` chars, so `Password="x"y";tail=4;` matches the whole `Password="x"y"`
    /// segment. No password tail leaks.
    /// </summary>
    [Fact]
    public void PA2_QuotedValueWithEmbeddedClose_FullyObfuscated()
    {
        // Input: Password="x"y";tail=4;
        // Regex matches the whole segment Password="x"y" -> replaced with Password=***;
        // The separate tail=4 key survives (it's not a credential).
        const string input = "Password=\"x\"y\";tail=4;";
        string result = PasswordObfuscator.Obfuscate(input);

        Assert.Contains("Password=***;", result);
        // No password tail leak — `y"` was part of the password value and is gone.
        Assert.DoesNotContain("y\";tail=4;", result);
    }

    /// <summary>
    /// INVERTED: an odd run of quote chars after Password=" is now fully consumed.
    /// The quoted branch matches `"` + `""`...`""` (pairs) + the final `"` + any
    /// trailing non-`;` chars, so no password characters leak.
    /// </summary>
    [Theory]
    [InlineData("Password=\"\"\"\"\"Hunter2!;X=1;")]   // 5 quotes — odd
    [InlineData("Password=\"\"\"\"\"\"\"Hunter2!;X=1;")]  // 7 quotes — odd
    [InlineData("Password=\"\"\"\"\"\"\"\"\"Hunter2!;X=1;")]  // 9 quotes — odd
    public void PA3_OddQuoteRun_FullyObfuscated(string input)
    {
        string result = PasswordObfuscator.Obfuscate(input);

        Assert.Contains("Password=***;", result);
        // No password tail leak.
        Assert.DoesNotContain("Hunter2!", result);
    }

    /// <summary>
    /// INVERTED: brace-branch now matches `{...}` AND any trailing non-`;` chars,
    /// so `Password={a}b};tail=5;` matches the whole `Password={a}b}` segment.
    /// No tail leak.
    /// </summary>
    [Fact]
    public void PA4_BraceBranch_FullyObfuscated()
    {
        // Input: Password={a}b};tail=5;
        // Regex brace branch: \{[^}]*\}[^;]* matches {a}b}
        const string input = "Password={a}b};tail=5;";
        string result = PasswordObfuscator.Obfuscate(input);

        Assert.Contains("Password=***;", result);
        Assert.DoesNotContain("b};tail=5;", result);
    }

    /// <summary>
    /// CONTROL: well-formed connection strings obfuscate correctly. This proves the
    /// gap is a crafted-input concern, not a default leak.
    /// </summary>
    [Theory]
    [InlineData("Server=x;Password=secret;Database=master;")]
    [InlineData("Server=x;Password=\"secret\";Database=master;")]
    [InlineData("Server=x;PWD=plain;Database=master;")]
    [InlineData("Server=x;AccessToken=abc123;Database=master;")]
    public void PA5_Control_NormalInputsObfuscateFully(string input)
    {
        string result = PasswordObfuscator.Obfuscate(input);
        Assert.DoesNotContain("secret", result);
        Assert.DoesNotContain("plain", result);
        Assert.DoesNotContain("abc123", result);
        Assert.Contains("Password=***;", result);
    }

    /// <summary>
    /// Regression: quoted password value containing a semicolon must be fully obfuscated.
    /// Input: Password="a;b";tail=4;
    /// The quoted value "a;b" contains a semicolon. The regex must consume the FULL
    /// quoted value (including the semicolon inside the quotes) before the closing "
    /// and trailing chars. The `tail=4` is a separate key-value, NOT part of the password.
    /// </summary>
    [Fact]
    public void PA6_QuotedValueWithSemicolon_FullyObfuscated()
    {
        const string input = "Password=\"a;b\";tail=4;";
        string result = PasswordObfuscator.Obfuscate(input);
        Assert.Contains("Password=***;", result);
        // The password value "a;b" must NOT leak — the semicolon inside quotes is part of the value
        Assert.DoesNotContain("a;b", result);
        // The tail key is a separate value and should be preserved
        Assert.Contains("tail=4", result);
    }
}
