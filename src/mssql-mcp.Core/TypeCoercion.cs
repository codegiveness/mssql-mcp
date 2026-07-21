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

        switch (value)
        {
            // bigint → string (avoids 2^53 precision loss in JSON/JS).
            case SqlInt64 sqlInt64:
                return sqlInt64.Value.ToString(CultureInfo.InvariantCulture);

            // decimal/numeric/money/smallmoney → string (preserves precision).
            case SqlDecimal sqlDecimal:
                return sqlDecimal.Value.ToString(CultureInfo.InvariantCulture);
            case SqlMoney sqlMoney:
                return sqlMoney.Value.ToString(CultureInfo.InvariantCulture);

            // int/smallint/tinyint/bit → JSON number.
            case SqlInt32 sqlInt32:
                return sqlInt32.Value;
            case SqlInt16 sqlInt16:
                return (long)sqlInt16.Value;
            case SqlByte sqlByte:
                return (long)sqlByte.Value;
            case SqlBoolean sqlBoolean:
                return sqlBoolean.Value ? 1L : 0L;

            // real/float → JSON number.
            case SqlSingle sqlSingle:
                return (double)sqlSingle.Value;
            case SqlDouble sqlDouble:
                return sqlDouble.Value;

            // date/datetime/datetime2/smalldatetime/datetimeoffset/time → ISO 8601 string.
            // SqlDateTime covers datetime/smalldatetime. date/datetime2 come back as DateTime,
            // datetimeoffset as DateTimeOffset, time as TimeSpan — handle all explicitly.
            case SqlDateTime sqlDateTime:
                return sqlDateTime.Value.ToString("o", CultureInfo.InvariantCulture);
            case DateTime dateTime:
                return dateTime.ToString("o", CultureInfo.InvariantCulture);
            case DateTimeOffset dateTimeOffset:
                return dateTimeOffset.ToString("o", CultureInfo.InvariantCulture);
            case TimeSpan timeSpan:
                return timeSpan.ToString("c", CultureInfo.InvariantCulture);

            // uniqueidentifier → canonical string.
            case SqlGuid sqlGuid:
                return sqlGuid.Value.ToString("D", CultureInfo.InvariantCulture);

            // binary/varbinary/image → base64 string.
            case SqlBinary sqlBinary:
                return Convert.ToBase64String(sqlBinary.Value, Base64FormattingOptions.None);

            // char/varchar/nchar/nvarchar/text/ntext → string.
            case SqlString sqlString:
                return sqlString.Value;

            // SqlXml (returned as SqlXml for xml columns).
            case SqlXml sqlXml:
                return sqlXml.Value;

            // Fallback: Sql* types not in the explicit list (geography/geometry/hierarchyid come back as objects).
            // Use .ToString() per ADR-0009.
            default:
                return value switch
                {
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                };
        }
    }

    /// <summary>
    /// Reads all columns of the current row as provider-specific Sql* values, coerces each,
    /// and returns a dictionary keyed by column name.
    /// </summary>
    public static Dictionary<string, object?> CoerceRow(SqlDataReader reader, string[] columnNames)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(columnNames);

        object[] values = new object[columnNames.Length];
        reader.GetProviderSpecificValues(values);

        Dictionary<string, object?> row = new(capacity: columnNames.Length);
        for (int i = 0; i < columnNames.Length; i++)
        {
            row[columnNames[i]] = Coerce(values[i]);
        }
        return row;
    }
}
