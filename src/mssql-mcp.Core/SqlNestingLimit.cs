using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace mssql_mcp.Core;

/// <summary>
/// Bounds the work ScriptDom's full parse can do, before it runs. The parser backtracks on
/// nested parentheses in table-source position, especially when the nesting is unbalanced or
/// otherwise erroneous: per statement, its cost grows about eightfold for every eight extra
/// levels (depth 16 ≈ 8 ms, depth 24 ≈ 80 ms, depth 32 ≈ 0.9 s), and error recovery at each
/// statement or batch boundary pays that cost again. Balanced, valid nesting stays cheap.
/// Tokenizing is linear, so these checks are cheap.
/// </summary>
/// <remarks>
/// Two limits apply. <see cref="MaxParenthesisDepth"/> caps the deepest nesting. The nesting
/// cost caps total work across the whole input: every opening parenthesis deeper than
/// <see cref="FreeDepth"/> adds 2^((depth − FreeDepth) / 4), matching the eightfold-per-eight-levels
/// growth, so one statement at depth 32 or several shallower ones fit within
/// <see cref="MaxNestingCost"/>, but repeated deep statements do not. Parentheses at depth
/// <see cref="FreeDepth"/> or less (function calls, IN lists, VALUES rows) cost nothing.
/// </remarks>
public static class SqlNestingLimit
{
    /// <summary>Deepest parenthesis nesting the Guard and the statement classifier will parse.</summary>
    public const int MaxParenthesisDepth = 32;

    /// <summary>Nesting depth up to which parentheses add no cost.</summary>
    public const int FreeDepth = 8;

    /// <summary>Total nesting cost allowed for one input; about one depth-32 statement.</summary>
    public const double MaxNestingCost = 400;

    /// <summary>
    /// Returns a rejection detail when <paramref name="sql"/> exceeds either limit, or
    /// <see langword="null"/> when the parser may run. Only parenthesis tokens count, not
    /// parentheses inside literals, comments or quoted identifiers.
    /// </summary>
    public static string? FindViolation(string sql)
    {
        (int maxDepth, double cost) = Measure(sql);
        if (maxDepth > MaxParenthesisDepth)
        {
            return $"Parenthesis nesting depth {maxDepth} exceeds the limit of {MaxParenthesisDepth}.";
        }
        return cost > MaxNestingCost
            ? $"Deeply nested parentheses exceed the parser work budget ({cost:F0} > {MaxNestingCost:F0})."
            : null;
    }

    /// <summary>Returns the deepest parenthesis nesting and the total nesting cost.</summary>
    public static (int MaxDepth, double Cost) Measure(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        TSql160Parser parser = new(initialQuotedIdentifiers: false);
        using StringReader reader = new(sql);
        int depth = 0;
        int maxDepth = 0;
        double cost = 0;
        foreach (TSqlParserToken token in parser.GetTokenStream(reader, out _))
        {
            if (token.TokenType == TSqlTokenType.LeftParenthesis)
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (depth > FreeDepth)
                {
                    cost += Math.Pow(2, (depth - FreeDepth) / 4.0);
                }
            }
            else if (token.TokenType == TSqlTokenType.RightParenthesis && depth > 0)
            {
                depth--;
            }
        }
        return (maxDepth, cost);
    }
}
