namespace mssql_mcp.Core.Configuration;

/// <summary>
/// Discriminated result of <see cref="CliDispatch.Dispatch"/>. One of
/// <see cref="Version"/>, <see cref="Help"/>, <see cref="RunServer"/>,
/// or <see cref="UnknownArgument"/> (ticket #59).
/// </summary>
public abstract record CliDispatchResult
{
    private CliDispatchResult() { }

    /// <summary>
    /// The user passed <c>--version</c>. Print the version and exit 0.
    /// </summary>
    public sealed record Version : CliDispatchResult;

    /// <summary>
    /// The user passed <c>--help</c> or <c>-h</c>. Print <see cref="CliDispatch.UsageText"/>
    /// and exit 0. Help takes precedence over <see cref="Version"/> and every other flag.
    /// </summary>
    public sealed record Help : CliDispatchResult;

    /// <summary>
    /// No version/help flag present. Proceed to <see cref="MssqlMcpOptions.Parse"/> and
    /// the normal server / validate startup path.
    /// </summary>
    public sealed record RunServer : CliDispatchResult;

    /// <summary>
    /// The user passed an unrecognized argument (ticket #59). Program.cs prints
    /// <c>mssql-mcp: unknown argument '&lt;Argument&gt;'.</c> and the usage block to stderr,
    /// then exits 1. Only the first unknown argument is reported.
    /// </summary>
    public sealed record UnknownArgument(string Argument) : CliDispatchResult;
}
