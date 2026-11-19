using System.Data.SqlTypes;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace mssql_mcp.Core;

/// <summary>
/// Coerces SQL Server provider-specific values (SqlInt64, SqlDecimal, SqlBinary, etc.)
/// into JSON-safe representations per ADR-0009.
/// </summary>
/// <remarks>
/// bigint → string (avoids 2^53 precision loss)
/// decimal/numeric/money/smallmoney → string (preserves precision)
/// date/time types → ISO 8601 string
/// binary types → base64 string
/// integer/float types → .NET number (int/long/double) — serialized as JSON number
/// NULL → null
/// Everything else → .ToString()
/// </remarks>
public static class TypeCoercion
{
    /// <summary>
    /// Coerces a single provider-specific value to its JSON-safe representation.
    /// Input is expected to be a Sql* type from GetProviderSpecificValues() (i.e. INullable).
    /// </summary>
    public static object? Coerce(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        if (value is INullable nullable && nullable.IsNull)
        {
            return null;
        }

        return value switch
        {
            // bigint → string (avoids 2^53 precision loss in JSON/JS).
            SqlInt64 sqlInt64 => sqlInt64.Value.ToString(CultureInfo.InvariantCulture),

            // decimal/numeric/money/smallmoney → string (preserves precision).
            // SqlDecimal supports 38 digits; converting through decimal overflows above 29.
            SqlDecimal sqlDecimal => sqlDecimal.ToString(),
            SqlMoney sqlMoney => sqlMoney.Value.ToString(CultureInfo.InvariantCulture),

            // int/smallint/tinyint/bit → JSON number.
            SqlInt32 sqlInt32 => sqlInt32.Value,
            SqlInt16 sqlInt16 => (long)sqlInt16.Value,
            SqlByte sqlByte => (long)sqlByte.Value,
            SqlBoolean sqlBoolean => sqlBoolean.Value ? 1L : 0L,

            // real/float → JSON number.
            SqlSingle sqlSingle => (double)sqlSingle.Value,
            SqlDouble sqlDouble => sqlDouble.Value,

            // date/datetime/datetime2/smalldatetime/datetimeoffset/time → ISO 8601 string.
            // SqlDateTime covers datetime/smalldatetime. date/datetime2 come back as DateTime,
            // datetimeoffset as DateTimeOffset, time as TimeSpan — handle all explicitly.
            SqlDateTime sqlDateTime => sqlDateTime.Value.ToString("o", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("o", CultureInfo.InvariantCulture),
            TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),

            // uniqueidentifier → canonical string.
            SqlGuid sqlGuid => sqlGuid.Value.ToString("D", CultureInfo.InvariantCulture),

            // binary/varbinary/image → base64 string.
            SqlBinary sqlBinary => Convert.ToBase64String(sqlBinary.Value, Base64FormattingOptions.None),

            // char/varchar/nchar/nvarchar/text/ntext → string.
            SqlString sqlString => sqlString.Value,

            // SqlXml (returned as SqlXml for xml columns).
            SqlXml sqlXml => sqlXml.Value,

            // Fallback: Sql* types not in the explicit list (geography/geometry/hierarchyid come back as objects).
            // Use .ToString() per ADR-0009.
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// Reads all columns of the current row as provider-specific Sql* values, coerces each,
    /// and returns a dictionary keyed by column name.
    /// </summary>
    public static Dictionary<string, object?> CoerceRow(SqlDataReader reader, string[] columnNames)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(columnNames);

        return CoerceRow(reader, columnNames, new object[columnNames.Length]);
    }

    internal static Dictionary<string, object?> CoerceRow(SqlDataReader reader, string[] columnNames, object[] values)
    {
        reader.GetProviderSpecificValues(values);

        Dictionary<string, object?> row = [with(capacity: columnNames.Length)];
        for (int i = 0; i < columnNames.Length; i++)
        {
            row[columnNames[i]] = Coerce(values[i]);
        }
        return row;
    }
}
