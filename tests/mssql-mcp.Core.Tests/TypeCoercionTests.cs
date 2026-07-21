using System.Data.SqlTypes;
using System.Text.Json;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// Tests for the ADR-0009 type coercion rules — each SQL type maps to the correct JSON representation.
/// </summary>
public class TypeCoercionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Coerce_SqlNull_Int64_ReturnsNull()
    {
        Assert.Null(TypeCoercion.Coerce(SqlInt64.Null));
    }

    [Fact]
    public void Coerce_BigInt_ReturnsStringToPreservePrecision()
    {
        // 2^53 + 1 — would lose precision if serialized as JSON number.
        SqlInt64 value = new(9_007_199_254_740_993L);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("9007199254740993", result);
    }

    [Fact]
    public void Coerce_Decimal_ReturnsStringToPreservePrecision()
    {
        SqlDecimal value = SqlDecimal.Parse("123456789.123456789");
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("123456789.123456789", result);
    }

    [Fact]
    public void Coerce_Money_ReturnsString()
    {
        SqlMoney value = new(12345.6789m);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("12345.6789", result);
    }

    [Fact]
    public void Coerce_Int32_ReturnsNumber()
    {
        SqlInt32 value = new(42);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Coerce_Int16_ReturnsLongNumber()
    {
        SqlInt16 value = new(32000);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal(32000L, result);
    }

    [Fact]
    public void Coerce_Byte_ReturnsLongNumber()
    {
        SqlByte value = new(255);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal(255L, result);
    }

    [Fact]
    public void Coerce_Boolean_ReturnsZeroOrOne()
    {
        Assert.Equal(1L, TypeCoercion.Coerce(new SqlBoolean(true)));
        Assert.Equal(0L, TypeCoercion.Coerce(new SqlBoolean(false)));
    }

    [Fact]
    public void Coerce_Float_ReturnsNumber()
    {
        SqlDouble value = new(3.14159);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal(3.14159, result);
    }

    [Fact]
    public void Coerce_Real_ReturnsDoubleNumber()
    {
        SqlSingle value = new(1.5f);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal(1.5, result);
    }

    [Fact]
    public void Coerce_DateTime_ReturnsIso8601String()
    {
        SqlDateTime value = new(2026, 7, 21, 13, 30, 45);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("2026-07-21T13:30:45.0000000", result);
    }

    [Fact]
    public void Coerce_Date_ClrDateTime_ReturnsIso8601String()
    {
        // date columns come back as DateTime (not SqlDateTime) from GetProviderSpecificValues.
        DateTime value = new(2026, 7, 21);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("2026-07-21T00:00:00.0000000", result);
    }

    [Fact]
    public void Coerce_DateTime2_ClrDateTime_ReturnsIso8601String()
    {
        // datetime2 columns come back as DateTime with fractional seconds.
        DateTime value = new(2026, 7, 21, 13, 30, 45, 123);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("2026-07-21T13:30:45.1230000", result);
    }

    [Fact]
    public void Coerce_DateTimeOffset_ReturnsIso8601String()
    {
        // datetimeoffset columns come back as DateTimeOffset.
        DateTimeOffset value = new(2026, 7, 21, 13, 30, 45, TimeSpan.FromHours(-5));
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("2026-07-21T13:30:45.0000000-05:00", result);
    }

    [Fact]
    public void Coerce_Time_TimeSpan_ReturnsIso8601String()
    {
        // time columns come back as TimeSpan.
        TimeSpan value = new(13, 30, 45);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("13:30:45", result);
    }

    [Fact]
    public void Coerce_Guid_ReturnsCanonicalString()
    {
        SqlGuid value = new("550e8400-e29b-41d4-a716-446655440000");
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", result);
    }

    [Fact]
    public void Coerce_Binary_ReturnsBase64String()
    {
        byte[] bytes = [0x01, 0x02, 0x03, 0xFF];
        SqlBinary value = new(bytes);
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("AQID/w==", result);
    }

    [Fact]
    public void Coerce_String_ReturnsString()
    {
        SqlString value = new("hello world");
        object? result = TypeCoercion.Coerce(value);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Coerce_Null_ReturnsNull()
    {
        Assert.Null(TypeCoercion.Coerce(null));
        Assert.Null(TypeCoercion.Coerce(DBNull.Value));
    }

    [Fact]
    public void Coerce_BigInt_RoundTripsThroughJsonAsString()
    {
        // The whole point of stringifying bigint is to survive JSON serialization.
        SqlInt64 value = new(long.MaxValue);
        object? coerced = TypeCoercion.Coerce(value);
        string json = JsonSerializer.Serialize(coerced, JsonOptions);
        // JSON string — not a bare number.
        Assert.Equal("\"" + long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"", json);
    }

    [Fact]
    public void Coerce_Int32_RoundTripsThroughJsonAsNumber()
    {
        SqlInt32 value = new(42);
        object? coerced = TypeCoercion.Coerce(value);
        string json = JsonSerializer.Serialize(coerced, JsonOptions);
        // JSON number, not a string.
        Assert.Equal("42", json);
    }
}
