using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

// CRITICAL: stdout is the MCP JSON-RPC transport — all logging MUST go to stderr.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Parse options (env + CLI precedence per ADR-0015). Fail fast on invalid config.
MssqlMcpOptions options;
try
{
    options = MssqlMcpOptions.Parse(args, Environment.GetEnvironmentVariables());
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// Register options both as concrete type (for direct injection) and via IOptions<T> (for
// tools that depend on IOptions<MssqlMcpOptions>). AddSingleton<T> alone does NOT populate
// IOptions<T> — the hosting framework's AddOptions() would create a default empty instance.
builder.Services.AddSingleton(options);
builder.Services.AddOptions<MssqlMcpOptions>()
    .Configure(o =>
    {
        o.ConnectionString = options.ConnectionString;
        o.AccessMode = options.AccessMode;
        o.QueryTimeout = options.QueryTimeout;
        o.LogLevel = options.LogLevel;
        o.LogFile = options.LogFile;
        o.MaxResultBytes = options.MaxResultBytes;
        o.RetryCount = options.RetryCount;
        o.RetryIntervalMin = options.RetryIntervalMin;
        o.RetryIntervalMax = options.RetryIntervalMax;
    });

builder.Services.AddSingleton<ISqlExecutor>(sp =>
    new SqlExecutor(options.ConnectionString, options.QueryTimeout,
        sp.GetRequiredService<ILogger<SqlExecutor>>()));

builder.Services.AddSingleton<IGuard>(sp =>
    new SqlGuard(options, sp.GetRequiredService<ILogger<SqlGuard>>()));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    // Explicitly specify the Tools assembly — WithToolsFromAssembly() with no args
    // uses Assembly.GetCallingAssembly() which returns the App assembly, not Tools.
    .WithToolsFromAssembly(typeof(mssql_mcp.Tools.DatabaseTools).Assembly);

await builder.Build().RunAsync();
return 0;
