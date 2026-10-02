using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace mssql_mcp.Core;

/// <summary>
/// Default <see cref="ISqlExecutor"/> using Microsoft.Data.SqlClient.
/// Opens a new SqlConnection per call, executes the query, and returns type-coerced rows per ADR-0009.
/// Transient connection and command failures are retried via Microsoft.Data.SqlClient's built-in
/// <see cref="SqlRetryLogicOption"/> (ADR-0004) — Microsoft maintains the transient error list.
/// </summary>
public sealed class SqlExecutor : ISqlExecutor
{
    private const string SetShowPlanOn = "SET SHOWPLAN_XML ON";
    private const string SetShowPlanOff = "SET SHOWPLAN_XML OFF";
    private const int ShowPlanCleanupTimeoutSeconds = 5;

    private readonly string _connectionString;
    private readonly int _commandTimeout;
    private readonly ILogger<SqlExecutor> _logger;
    private readonly SqlRetryLogicBaseProvider _retryProvider;

    public SqlExecutor(
        string connectionString,
        int commandTimeout,
        int retryCount,
        int retryIntervalMin,
        int retryIntervalMax,
        ILogger<SqlExecutor> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (commandTimeout < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeout), "Command timeout must be non-negative.");
        }
        _connectionString = connectionString;
        _commandTimeout = commandTimeout;
        _logger = logger;
        _retryProvider = BuildRetryProvider(retryCount, retryIntervalMin, retryIntervalMax, logger);
    }

    /// <summary>
    /// Builds the <see cref="SqlRetryLogicOption"/> for Microsoft.Data.SqlClient's exponential
    /// backoff retry provider (ADR-0004). Microsoft maintains the transient-error list —
    /// we only configure the count and backoff range.
    /// </summary>
    /// <remarks>
    /// Exposed as internal so tests can verify retry configuration without opening connections.
    /// </remarks>
    internal static SqlRetryLogicOption BuildRetryOption(int retryCount, int retryIntervalMin, int retryIntervalMax)
    {
        // NumberOfTries is total attempts including the first, so RetryCount=N → NumberOfTries=N+1.
        // SqlRetryLogicOption enforces 1 ≤ NumberOfTries ≤ 60; MssqlMcpOptions.Parse rejects RetryCount > 59.
        // MinTimeInterval must be strictly less than MaxTimeInterval; both ≤ 120s — also validated at Parse.
        return new SqlRetryLogicOption
        {
            NumberOfTries = retryCount + 1,
            DeltaTime = TimeSpan.FromSeconds(retryIntervalMin),
            MinTimeInterval = TimeSpan.FromSeconds(retryIntervalMin),
            MaxTimeInterval = TimeSpan.FromSeconds(retryIntervalMax),
        };
    }

    internal static SqlRetryLogicBaseProvider BuildRetryProvider(
        int retryCount,
        int retryIntervalMin,
        int retryIntervalMax,
        ILogger logger)
    {
        if (retryCount <= 0)
        {
            // Explicitly disable retries for this executor without changing process-global state.
            return SqlConfigurableRetryFactory.CreateNoneRetryProvider();
        }

        SqlRetryLogicOption option = BuildRetryOption(retryCount, retryIntervalMin, retryIntervalMax);
        SqlRetryLogicBaseProvider provider = SqlConfigurableRetryFactory.CreateExponentialRetryProvider(option);

        // SqlRetryingEventArgs.RetryCount starts at 1 after the first failure, so it matches
        // the human-readable "attempt N of Max" without further adjustment.
        provider.Retrying += (_, args) =>
        {
            Exception last = args.Exceptions.Count > 0 ? args.Exceptions[^1] : new InvalidOperationException("Unknown retry exception");
            int number = last is SqlException sqlEx && sqlEx.Errors.Count > 0 ? sqlEx.Errors[0].Number : 0;
            logger.LogInformation(
                "[retry] Transient error {Number}, attempt {Attempt} of {Max}, waiting {DelayMs}ms",
                number, args.RetryCount, retryCount, (int)args.Delay.TotalMilliseconds);
        };

        return provider;
    }

    public async Task<SqlQueryResult> ExecuteQueryAsync(string sql, long maxResultBytes, CancellationToken ct)
    {
        return await ExecuteQueryAsync(sql, parameters: null, maxResultBytes, ct).ConfigureAwait(false);
    }

    public async Task<SqlQueryResult> ExecuteQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object>? parameters,
        long maxResultBytes,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResultBytes);

        await using SqlConnection connection = new(_connectionString) { RetryLogicProvider = _retryProvider };
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using SqlCommand command = new(sql, connection)
        {
            CommandTimeout = _commandTimeout,
            RetryLogicProvider = _retryProvider,
        };

        if (parameters is { Count: > 0 })
        {
            foreach (KeyValuePair<string, object> p in parameters)
            {
                command.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
            }
        }

        await using SqlDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        string[] columnNames = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames[i] = reader.GetName(i);
        }

        object[] values = new object[columnNames.Length];
        List<Dictionary<string, object?>> rows = new();
        long retainedBytes = 2; // Reserve both array brackets, even for an empty result.
        long rowStructureBytes = maxResultBytes > 0 ? ResultByteBudget.RowStructureBytes(columnNames) : 0;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            Dictionary<string, object?> row = TypeCoercion.CoerceRow(reader, columnNames, values);
            if (maxResultBytes > 0)
            {
                long nextBytes = ResultByteBudget.Add(ResultByteBudget.RowBytes(row, rowStructureBytes), rows.Count == 0 ? 0 : 1);
                if (retainedBytes > maxResultBytes || nextBytes > maxResultBytes - retainedBytes)
                {
                    command.Cancel();
                    return new SqlQueryResult(rows, IsTruncated: true);
                }
                retainedBytes += nextBytes;
            }
            rows.Add(row);
        }

        return new SqlQueryResult(rows, IsTruncated: false);
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        await using SqlConnection connection = new(_connectionString) { RetryLogicProvider = _retryProvider };
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using SqlCommand command = new(sql, connection)
        {
            CommandTimeout = _commandTimeout,
            RetryLogicProvider = _retryProvider,
        };

        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes <c>SET SHOWPLAN_XML ON</c>, runs <paramref name="sql"/>, and returns the
    /// SHOWPLAN_XML string. The query is not actually executed — SQL Server returns the
    /// estimated plan as a single-row, single-column XML result set. <c>SET SHOWPLAN_XML OFF</c>
    /// is always run in a finally block so the session-scoped setting cannot leak onto a
    /// pooled connection. Cleanup ignores request cancellation but has an independent five-second
    /// limit, and discards the pool if OFF fails. A positive raw XML budget refuses oversized
    /// plans while reading; summary callers pass zero and retain the full plan.
    /// </summary>
    public async Task<string> ExecuteShowPlanXmlAsync(string sql, long maxResultBytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResultBytes);

        await using SqlConnection connection = new(_connectionString) { RetryLogicProvider = _retryProvider };
        await connection.OpenAsync(ct).ConfigureAwait(false);

        try
        {
            await using (SqlCommand onCommand = new(SetShowPlanOn, connection)
            {
                CommandTimeout = _commandTimeout,
                RetryLogicProvider = _retryProvider,
            })
            {
                await onCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using SqlCommand planCommand = new(sql, connection)
            {
                CommandTimeout = _commandTimeout,
                RetryLogicProvider = _retryProvider,
            };
            using CancellationTokenRegistration cancellation = ct.Register(static state => ((SqlCommand)state!).Cancel(), planCommand);
            await using SqlDataReader reader = await planCommand.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);

            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("SHOWPLAN_XML returned no rows.");
            }

            if (reader.IsDBNull(0))
            {
                return string.Empty;
            }
            using BoundedPlanWriter buffer = new(maxResultBytes, ct);
            try
            {
                if (string.Equals(reader.GetDataTypeName(0), "xml", StringComparison.OrdinalIgnoreCase))
                {
                    using System.Xml.XmlReader xml = reader.GetXmlReader(0);
                    using System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(buffer, new System.Xml.XmlWriterSettings
                    {
                        OmitXmlDeclaration = true,
                        ConformanceLevel = System.Xml.ConformanceLevel.Fragment,
                    });
                    writer.WriteNode(xml, defattr: true);
                    writer.Flush();
                }
                else
                {
                    using TextReader text = reader.GetTextReader(0);
                    char[] chunk = new char[4096];
                    int count;
                    while ((count = await text.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false)) != 0)
                    {
                        buffer.Write(chunk, 0, count);
                    }
                }
                ct.ThrowIfCancellationRequested();
                return buffer.ToString();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                planCommand.Cancel();
                throw new OperationCanceledException(ct);
            }
            catch
            {
                planCommand.Cancel();
                throw;
            }
        }
        catch (SqlException ex) when (ct.IsCancellationRequested)
        {
            // Cancel can surface a SQL error while metadata is still being read,
            // before the streaming reader's cancellation boundary is reached.
            throw new OperationCanceledException("Query plan request was canceled.", ex, ct);
        }
        finally
        {
            // Always reset SHOWPLAN_XML off — leaving it ON corrupts every subsequent query on
            // the pooled connection (they would return plan XML instead of rows).
            // Do not mask the original failure. Bound cleanup independently of request cancellation
            // and QueryTimeout=0; discard the pool if session cleanup fails.
            try
            {
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(ShowPlanCleanupTimeoutSeconds));
                await using SqlCommand offCommand = new(SetShowPlanOff, connection)
                {
                    CommandTimeout = _commandTimeout > 0 ? Math.Min(_commandTimeout, ShowPlanCleanupTimeoutSeconds) : ShowPlanCleanupTimeoutSeconds,
                    RetryLogicProvider = _retryProvider,
                };
                await offCommand.ExecuteNonQueryAsync(cleanup.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SqlConnection.ClearPool(connection);
                _logger.LogWarning(ex, "[showplan] SET SHOWPLAN_XML OFF failed; discarding connection pool");
            }
        }
    }
}
