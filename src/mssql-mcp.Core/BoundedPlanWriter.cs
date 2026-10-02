using System.Text;

namespace mssql_mcp.Core;

/// <summary>Raw XML is refused rather than cut into an invalid document.</summary>
public sealed class PlanTooLargeException(long maxBytes) : Exception("Raw query plan exceeds the configured byte limit.")
{
    public long MaxBytes { get; } = maxBytes;
}

/// <summary>Accumulates streamed XML, checking UTF-8 size before appending each chunk.</summary>
internal sealed class BoundedPlanWriter(long maxBytes, CancellationToken ct) : TextWriter
{
    private readonly StringBuilder _buffer = new();
    private long _bytes;
    private bool _highSurrogate;
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        Span<char> character = stackalloc char[1];
        character[0] = value;
        Write(character);
    }

    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));
    public override void Write(string? value)
    {
        if (value is not null)
        {
            Write(value.AsSpan());
        }
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        ct.ThrowIfCancellationRequested();
        long bytes = _bytes;
        bool highSurrogate = _highSurrogate;
        foreach (char c in buffer)
        {
            int width = c <= 0x7f ? 1 : c <= 0x7ff ? 2 : char.IsLowSurrogate(c) && highSurrogate ? 1 : 3;
            bytes = ResultByteBudget.Add(bytes, width);
            highSurrogate = char.IsHighSurrogate(c);
            if (maxBytes > 0 && bytes > maxBytes)
            {
                throw new PlanTooLargeException(maxBytes);
            }
        }
        _bytes = bytes;
        _highSurrogate = highSurrogate;
        _buffer.Append(buffer);
    }

    public override string ToString() => _buffer.ToString();
}
