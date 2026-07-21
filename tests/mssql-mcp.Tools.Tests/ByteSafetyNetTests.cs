using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Unit tests for the byte-size transport safety net (ADR-0003 amendment). The cap
/// serializes rows incrementally and stops when the accumulated UTF-8 byte count crosses
/// the threshold, returning the partial array plus a truncation notice as a second
/// TextContentBlock. <c>MSSQL_MAX_RESULT_BYTES=0</c> disables the cap.
/// </summary>
public class ByteSafetyNetTests
{
    private const long Default10Mb = 10 * 1024 * 1024;

    private static List<Dictionary<string, object?>> RowsOfSize(int rowCount, int approxBytesPerRow)
    {
        // Each row is {"pad":"..."} where the pad string is sized so the serialized row
        // (including braces, quotes, colon, comma) is approximately approxBytesPerRow.
        // Serialized shape: {"pad":"<content>"}  -> overhead is ~11 bytes.
        int padLen = Math.Max(0, approxBytesPerRow - 11);
        string pad = new('x', padLen);
        List<Dictionary<string, object?>> rows = new(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            rows.Add(new() { ["pad"] = pad });
        }
        return rows;
    }

    private static string GetText(CallToolResult result, int index)
    {
        Assert.NotNull(result.Content);
        Assert.InRange(index, 0, result.Content.Count - 1);
        return Assert.IsType<TextContentBlock>(result.Content[index]).Text;
    }

    [Fact]
    public void ByteCap_UnderThreshold_ReturnsAllRows()
    {
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 10, approxBytesPerRow: 100);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, Default10Mb);

        Assert.Single(result.Content);
        Assert.False(result.IsError);
        string json = GetText(result, 0);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(10, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void ByteCap_AtThreshold_TruncatesWithNotice()
    {
        // 1000 rows of ~100KB = ~100MB total. 10MB cap must truncate.
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 1000, approxBytesPerRow: 100_000);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, Default10Mb);

        Assert.Equal(2, result.Content.Count);
        Assert.False(result.IsError);

        // First content is the truncated JSON array (must still parse).
        string json = GetText(result, 0);
        using JsonDocument doc = JsonDocument.Parse(json);
        int returned = doc.RootElement.GetArrayLength();
        Assert.InRange(returned, 1, 999);

        // Second content is the truncation notice.
        string notice = GetText(result, 1);
        Assert.StartsWith("[truncated]", notice);
        Assert.Contains($"{Default10Mb} bytes", notice);
        Assert.Contains($"{returned} rows returned", notice);
        Assert.Contains("Narrow with WHERE", notice);
    }

    [Fact]
    public void ByteCap_Disabled_WhenZero()
    {
        // maxBytes=0 disables the cap entirely — all rows returned, single content.
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 50, approxBytesPerRow: 1000);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, maxBytes: 0);

        Assert.Single(result.Content);
        Assert.False(result.IsError);
        string json = GetText(result, 0);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(50, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void ByteCap_CustomThreshold_1MB()
    {
        // 50 rows of ~50KB = ~2.5MB. 1MB cap truncates earlier than 10MB would.
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 50, approxBytesPerRow: 50_000);
        const long OneMb = 1024 * 1024;

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, OneMb);

        Assert.Equal(2, result.Content.Count);
        string json = GetText(result, 0);
        using JsonDocument doc = JsonDocument.Parse(json);
        int returned = doc.RootElement.GetArrayLength();
        // 1MB / 50KB per row ~= 20 rows max.
        Assert.InRange(returned, 1, 30);

        string notice = GetText(result, 1);
        Assert.Contains($"{OneMb} bytes", notice);
    }

    [Fact]
    public void ByteCap_NoticeText_Format()
    {
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 100, approxBytesPerRow: 200_000);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, Default10Mb);

        Assert.Equal(2, result.Content.Count);
        string notice = GetText(result, 1);

        // Notice format: [truncated] Result exceeded {maxBytes} bytes. {returned} rows returned, more exist. Narrow with WHERE, TOP, or OFFSET/FETCH.
        Match m = Regex.Match(notice, @"^\[truncated\] Result exceeded (\d+) bytes\. (\d+) rows returned, more exist\. Narrow with WHERE, TOP, or OFFSET/FETCH\.$");
        Assert.True(m.Success, $"Notice did not match expected format: {notice}");
        Assert.Equal(Default10Mb.ToString(), m.Groups[1].Value);
        int returned = int.Parse(m.Groups[2].Value);
        Assert.True(returned > 0, "Truncated row count should be positive");
    }

    [Fact]
    public void ByteCap_DataFirst_NoticeSecond()
    {
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 10, approxBytesPerRow: 2_000_000);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, Default10Mb);

        Assert.Equal(2, result.Content.Count);

        // First item is the JSON array data.
        Assert.IsType<TextContentBlock>(result.Content[0]);
        string first = GetText(result, 0);
        Assert.StartsWith("[", first);
        Assert.EndsWith("]", first);
        using JsonDocument doc = JsonDocument.Parse(first);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        // Second item is the truncation notice (not valid JSON array).
        Assert.IsType<TextContentBlock>(result.Content[1]);
        string second = GetText(result, 1);
        Assert.StartsWith("[truncated]", second);
        Assert.False(second.StartsWith("[{"), $"Second content looks like row data, not notice: {second.Substring(0, Math.Min(40, second.Length))}");
    }

    [Fact]
    public void ByteCap_EmptyResult_ReturnsEmptyArray()
    {
        List<Dictionary<string, object?>> rows = new(0);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, Default10Mb);

        Assert.Single(result.Content);
        Assert.False(result.IsError);
        string json = GetText(result, 0);
        Assert.Equal("[]", json);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void ByteCap_StringOverload_UnderThreshold_ReturnsText()
    {
        string xml = "<plan>small</plan>";

        CallToolResult result = ToolErrors.SuccessWithByteCap(xml, Default10Mb);

        Assert.Single(result.Content);
        Assert.False(result.IsError);
        Assert.Equal(xml, GetText(result, 0));
    }

    [Fact]
    public void ByteCap_StringOverload_OverThreshold_TruncatesAtCharBoundary()
    {
        // Build a string longer than 1MB when UTF-8 encoded (all ASCII so char == byte).
        string big = new('x', 2_000_000);
        const long OneMb = 1024 * 1024;

        CallToolResult result = ToolErrors.SuccessWithByteCap(big, OneMb);

        Assert.Equal(2, result.Content.Count);
        Assert.False(result.IsError);

        string truncated = GetText(result, 0);
        int truncatedBytes = System.Text.Encoding.UTF8.GetByteCount(truncated);
        Assert.True(truncatedBytes <= OneMb, $"Truncated bytes {truncatedBytes} exceeded threshold {OneMb}");

        string notice = GetText(result, 1);
        Assert.StartsWith("[truncated]", notice);
        Assert.Contains($"{OneMb} bytes", notice);
    }

    [Fact]
    public void ByteCap_StringOverload_Disabled_WhenZero()
    {
        string big = new('y', 100_000);

        CallToolResult result = ToolErrors.SuccessWithByteCap(big, maxBytes: 0);

        Assert.Single(result.Content);
        Assert.Equal(big, GetText(result, 0));
    }

    [Fact]
    public void ByteCap_MixedObjectList_SerializesAll()
    {
        // list_objects returns a List<object> where elements may be row dicts or notice objects.
        // The covariant IReadOnlyList<object> overload must handle both without copying.
        List<object> payload = new()
        {
            new { notice = "row limit hit", limit = 1000 },
            new Dictionary<string, object?> { ["name"] = "t1", ["schema"] = "dbo" },
            new Dictionary<string, object?> { ["name"] = "t2", ["schema"] = "dbo" },
        };

        CallToolResult result = ToolErrors.SuccessWithByteCap(payload, Default10Mb);

        Assert.Single(result.Content);
        string json = GetText(result, 0);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void ByteCap_PassesNullLogger_WithoutThrowing()
    {
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 5, approxBytesPerRow: 200_000);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, maxBytes: 1000, logger: null);

        Assert.Equal(2, result.Content.Count);
    }

    [Fact]
    public void ByteCap_Logger_ReceivesWarning_OnTruncation()
    {
        // Use a capturing logger to verify the warning fires.
        TestLogger logger = new();
        List<Dictionary<string, object?>> rows = RowsOfSize(rowCount: 10, approxBytesPerRow: 200_000);

        CallToolResult result = ToolErrors.SuccessWithByteCap(rows, maxBytes: 500_000, logger);

        Assert.Equal(2, result.Content.Count);
        Assert.True(logger.WarningReceived);
    }

    private sealed class TestLogger : Microsoft.Extensions.Logging.ILogger
    {
        public bool WarningReceived { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                WarningReceived = true;
            }
        }
    }
}
