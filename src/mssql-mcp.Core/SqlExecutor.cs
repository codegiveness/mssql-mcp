using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace mssql_mcp.Core;

/// <summary>
/// Default <see cref="ISqlExecutor"/> using Microsoft.Data.SqlClient.
/// Opens a new SqlConnection per call, executes the query, and returns type-coerced rows per ADR-0009.
/// </summary>
public sealed class SqlExecutor : ISqlExecutor
{
    private readonly string _connectionString;
    private readonly int _commandTimeout;
    private readonly ILogger<SqlExecutor> _logger;

    public SqlExecutor(string connectionString, int commandTimeout, ILogger<SqlExecutor> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (commandTimeout < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeout), "Command timeout must be non-negative.");
        }
        _connectionString = connectionString;
        _commandTimeout = commandTimeout;
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, CancellationToken ct)
    {
        return await ExecuteQueryAsync(sql, parameters: null, ct).ConfigureAwait(false);
    }

    public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object>? parameters,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        using SqlCommand command = new(sql, connection)
        {
            CommandTimeout = _commandTimeout,
        };

        if (parameters is { Count: > 0 })
        {
            foreach (KeyValuePair<string, object> p in parameters)
            {
                command.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
            }
        }

        using SqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        string[] columnNames = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames[i] = reader.GetName(i);
        }

        List<Dictionary<string, object?>> rows = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            Dictionary<string, object?> row = TypeCoercion.CoerceRow(reader, columnNames);
            rows.Add(row);
        }

        return rows;
    }
}
