using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using mssql_mcp.Core;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Tools;

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

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<MssqlMcpOptions>(options);

builder.Services.AddSingleton<ISqlExecutor>(sp =>
    new SqlExecutor(options.ConnectionString, options.QueryTimeout,
        sp.GetRequiredService<ILogger<SqlExecutor>>()));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;
