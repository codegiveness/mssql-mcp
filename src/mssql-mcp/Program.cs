using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using mssql_mcp.Core.Logging;

// CRITICAL: stdout is the MCP JSON-RPC transport — all logging MUST go to stderr.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Strip the default console logger so we can install our own (stderr + obfuscation).
// Host.CreateApplicationBuilder adds a ConsoleLoggerProvider by default.
builder.Logging.ClearProviders();

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

// --validate: pre-flight connection check — open, SELECT 1, close, exit 0/1.
// Does NOT start the MCP stdio server (stdout stays clean for the protocol).
if (options.Validate)
{
    using CancellationTokenSource validateCts = new(TimeSpan.FromMinutes(2));
    (bool ok, string message) = await ConnectionValidator.ValidateAsync(options, validateCts.Token);
    Console.Error.WriteLine(message);
    return ok ? 0 : 1;
}

// Parse log level (CLI > env > default "info"). Fail fast on invalid value per ADR-0015.
LogLevel logLevel;
try
{
    logLevel = LogLevelParser.Parse(options.LogLevel);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

LoggingSetup.Configure(builder.Logging, logLevel, options.LogFile);

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
        o.Validate = options.Validate;
    });

builder.Services.AddSingleton<ISqlExecutor>(sp =>
    new SqlExecutor(options.ConnectionString, options.QueryTimeout,
        options.RetryCount, options.RetryIntervalMin, options.RetryIntervalMax,
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
