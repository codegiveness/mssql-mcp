using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using mssql_mcp.Core.Logging;
using mssql_mcp.Tools;

// --version: print the assembly version and exit 0. No DB connection required.
if (args.Contains("--version"))
{
    string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    Console.WriteLine("mssql-mcp " + version);
    return 0;
}

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

LoggingSetup.Configure(builder.Logging, logLevel, options.LogFile, options.LogFileMaxBytes, options.LogFileMaxRolls);

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
        o.LogFileMaxBytes = options.LogFileMaxBytes;
        o.LogFileMaxRolls = options.LogFileMaxRolls;
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

// Programmatic tool registration (ADR-0017). Replaces WithToolsFromAssembly so the
// execute_sql annotations can vary by AccessMode at runtime: ReadOnly=false/Destructive=true
// in Unrestricted mode, ReadOnly=true/Destructive=false in Restricted mode. The [McpServerTool]
// attributes on the tool classes stay as documentation of intent but are overridden here by
// McpServerToolCreateOptions (DeriveOptions uses ??=, so passed-in options win over attributes).
// Each tool is registered as a factory (Func<IServiceProvider, McpServerTool>) so the runtime
// app provider is passed to McpServerToolCreateOptions.Services, matching the SDK's internal
// WithToolsFromAssembly behavior. The createTargetFunc resolves the tool class from that same
// provider per invocation via ActivatorUtilities.CreateInstance.
bool unrestricted = options.AccessMode == AccessMode.Unrestricted;

static void AddTool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicConstructors)] TTool>(IServiceCollection services, string methodName, bool? readOnly, bool? destructive)
    where TTool : class
{
    MethodInfo method = typeof(TTool).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Tool method '{methodName}' not found on {typeof(TTool).Name}.");
    services.AddSingleton(sp => McpServerTool.Create(
        method,
        static r => ActivatorUtilities.CreateInstance(r.Services!, typeof(TTool)),
        new McpServerToolCreateOptions
        {
            Services = sp,
            ReadOnly = readOnly,
            Destructive = destructive,
            Idempotent = false,
            OpenWorld = false,
        }));
}

// Discovery (4) — read-only in both modes.
AddTool<DatabaseTools>(builder.Services, nameof(DatabaseTools.ListDatabases), readOnly: true, destructive: false);
AddTool<DatabaseTools>(builder.Services, nameof(DatabaseTools.ListSchemas), readOnly: true, destructive: false);
AddTool<DatabaseTools>(builder.Services, nameof(DatabaseTools.ListObjects), readOnly: true, destructive: false);
AddTool<DatabaseTools>(builder.Services, nameof(DatabaseTools.GetObjectDetails), readOnly: true, destructive: false);
// SQL (2) — execute_sql varies by mode; explain_query is always read-only.
AddTool<SqlTools>(builder.Services, nameof(SqlTools.ExecuteSql),
    readOnly: !unrestricted,
    destructive: unrestricted);
AddTool<PlanTools>(builder.Services, nameof(PlanTools.ExplainQuery), readOnly: true, destructive: false);
// Ops (3) — read-only in both modes.
AddTool<OpsTools>(builder.Services, nameof(OpsTools.AnalyzeIndexes), readOnly: true, destructive: false);
AddTool<OpsTools>(builder.Services, nameof(OpsTools.GetTopQueries), readOnly: true, destructive: false);
AddTool<OpsTools>(builder.Services, nameof(OpsTools.AnalyzeDbHealth), readOnly: true, destructive: false);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();

await builder.Build().RunAsync();
return 0;
