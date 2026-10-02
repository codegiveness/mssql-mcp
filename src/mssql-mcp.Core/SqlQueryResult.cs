namespace mssql_mcp.Core;

/// <summary>Retained rows and whether the reader stopped before retaining a further row.</summary>
public sealed record SqlQueryResult(List<Dictionary<string, object?>> Rows, bool IsTruncated);

/// <summary>Allocation-free upper bounds for the JSON-safe values produced by TypeCoercion.</summary>
internal static class ResultByteBudget
{
    public static long Add(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;

    public static long RowStructureBytes(string[] columnNames)
    {
        long bytes = 2;
        for (int i = 0; i < columnNames.Length; i++)
        {
            // Counting duplicate names too is conservative when CoerceRow overwrites a key.
            bytes = Add(bytes, i == 0 ? 1 : 2); // colon, and comma after first field
            bytes = Add(bytes, StringBytes(columnNames[i]));
        }
        return bytes;
    }

    public static long RowBytes(Dictionary<string, object?> row, long structureBytes)
    {
        long bytes = structureBytes;
        foreach (KeyValuePair<string, object?> field in row)
        {
            bytes = Add(bytes, field.Value switch
            {
                null => 4,
                string text => StringBytes(text),
                int => 11,
                long => 20,
                double => 32,
                bool => 5,
                _ => throw new InvalidOperationException("Unsupported coerced result type."),
            });
        }
        return bytes;
    }

    private static long StringBytes(string text)
    {
        long bytes = 2; // quotes
        foreach (char c in text)
        {
            // Default System.Text.Json escapes non-ASCII, controls and HTML-sensitive ASCII.
            // Six bytes per UTF-16 unit also covers surrogate pairs and invalid Unicode.
            bytes = Add(bytes, c is >= ' ' and <= '~' && c is not ('"' or '\\' or '<' or '>' or '&' or '\'' or '+' or '`') ? 1 : 6);
        }
        return bytes;
    }
}
