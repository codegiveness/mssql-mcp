namespace mssql_mcp.Core.Configuration;

/// <summary>
/// Pure pre-<see cref="MssqlMcpOptions.Parse"/> argument dispatcher. Decides whether
/// the binary should print its version, print usage help, or proceed to server startup —
/// without needing a connection string or env access. See ADR-0031.
/// </summary>
public static class CliDispatch
{
    /// <summary>
    /// Full usage block printed by <c>--help</c>/<c>-h</c> and by the unknown-argument
    /// error path (ticket #59). Lists every recognized flag, its env var equivalent,
    /// its default, and the update instructions for the most common unknown command.
    /// </summary>
    public const string UsageText = """
        Usage: mssql-mcp [options]
          (no args)              Start the MCP stdio server (default)
          --version              Print version and exit
          --help, -h             Print this help and exit
          --validate             Test the SQL Server connection and exit
          --connection-string    SQL Server connection string (env: MSSQL_CONNECTION_STRING)
          --access-mode          restricted | unrestricted (default: restricted, env: MSSQL_ACCESS_MODE)
          --query-timeout        Per-query timeout in seconds (default: 30, env: MSSQL_QUERY_TIMEOUT)
          --log-level            trace | debug | info | warning | error | critical (default: info, env: MSSQL_LOG_LEVEL)

        To update mssql-mcp:
          npm install -g @codegiveness/mssql-mcp@latest
          # or
          dotnet tool update -g codegiveness.mssql-mcp
        """;

    // Value-taking flags: when in "--flag value" (space-separated) form, the next token
    // is the value and must not be mistaken for a flag. In "--flag=value" form the value
    // is embedded and no extra token is consumed.
    private static readonly HashSet<string> ValueFlags = new(StringComparer.Ordinal)
    {
        MssqlMcpOptions.CliConnectionString,
        MssqlMcpOptions.CliAccessMode,
        MssqlMcpOptions.CliQueryTimeout,
        MssqlMcpOptions.CliLogLevel,
    };

    /// <summary>
    /// Inspects <paramref name="args"/> and returns the dispatch decision.
    /// <see cref="CliDispatchResult.Help"/> takes precedence over every other flag
    /// (including <see cref="CliDispatchResult.Version"/> and unknown-argument errors).
    /// <see cref="CliDispatchResult.Version"/> takes precedence over unknown-argument
    /// errors so existing <c>--version</c> behavior is unchanged. Unrecognized arguments
    /// (when no version or help flag is present) return
    /// <see cref="CliDispatchResult.UnknownArgument"/> with the first offending token.
    /// </summary>
    public static CliDispatchResult Dispatch(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool versionFound = false;
        string? unknownArgument = null;

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];

            // --flag=value form: the value is embedded; never consumes the next token.
            // Check the flag part for help/version, then move on.
            int eq = token.IndexOf('=');
            if (eq >= 0)
            {
                string flagPart = token[..eq];
                if (IsHelpFlag(flagPart))
                {
                    return new CliDispatchResult.Help();
                }
                if (flagPart == "--version")
                {
                    versionFound = true;
                    continue;
                }
                if (flagPart == MssqlMcpOptions.CliValidate || ValueFlags.Contains(flagPart))
                {
                    continue;
                }
                // Report the flag part (e.g. "--bogus" from "--bogus=foo"), not the full
                // token — matches the bare-flag path at line 113 for consistency.
                unknownArgument ??= flagPart;
                continue;
            }

            // Exact-token boolean switches (no value to consume).
            if (IsHelpFlag(token))
            {
                return new CliDispatchResult.Help();
            }
            if (token == "--version")
            {
                versionFound = true;
                continue;
            }
            if (token == MssqlMcpOptions.CliValidate)
            {
                continue;
            }

            // Value-taking flag in space form: consume the next token as its value so
            // a value that looks like a flag (e.g. "--connection-string --version") is
            // not mistaken for the version switch.
            if (ValueFlags.Contains(token))
            {
                if (i + 1 < args.Length)
                {
                    i++;
                }
                continue;
            }

            unknownArgument ??= token;
        }

        if (versionFound)
        {
            return new CliDispatchResult.Version();
        }
        if (unknownArgument is not null)
        {
            return new CliDispatchResult.UnknownArgument(unknownArgument);
        }
        return new CliDispatchResult.RunServer();
    }

    private static bool IsHelpFlag(string token) => token == "--help" || token == "-h";
}
