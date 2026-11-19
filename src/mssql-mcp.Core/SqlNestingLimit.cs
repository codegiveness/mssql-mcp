using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace mssql_mcp.Core;

/// <summary>
/// Bounds parenthesis nesting before ScriptDom's full parse. The parser backtracks on nested
/// parentheses in table-source position, and its cost grows about eightfold for every eight
/// extra levels: depth 32 parses in under a second, while a 438-byte fuzzer input with depth 176
/// took 30 seconds of CPU. Tokenizing is linear, so the depth check is cheap.
/// </summary>
public static class SqlNestingLimit
{
    /// <summary>Deepest parenthesis nesting the Guard and the statement classifier will parse.</summary>
    public const int MaxParenthesisDepth = 32;

    /// <summary>
    /// Returns the deepest parenthesis nesting in <paramref name="sql"/>, counting only
    /// parenthesis tokens (not parentheses inside literals, comments or quoted identifiers).
    /// </summary>
    public static int MeasureDepth(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        TSql160Parser parser = new(initialQuotedIdentifiers: false);
        using StringReader reader = new(sql);
        int depth = 0;
        int max = 0;
        foreach (TSqlParserToken token in parser.GetTokenStream(reader, out _))
        {
            if (token.TokenType == TSqlTokenType.LeftParenthesis)
            {
                depth++;
                max = Math.Max(max, depth);
            }
            else if (token.TokenType == TSqlTokenType.RightParenthesis && depth > 0)
            {
                depth--;
            }
        }
        return max;
    }
}
