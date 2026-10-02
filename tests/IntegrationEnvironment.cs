namespace mssql_mcp.Tests;

public static class IntegrationEnvironment
{
    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"));
}
