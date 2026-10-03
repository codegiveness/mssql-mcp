using MssqlMcp.RepoTool;

namespace MssqlMcp.RepoTool.Tests;

[CollectionDefinition("Repository environment", DisableParallelization = true)]
public sealed class RepositoryEnvironmentScope;

[Collection("Repository environment")]
public sealed class RepoContextTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("repo-context-tests-").FullName;
    private readonly string? _connection = Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");

    [Fact]
    public void LoadsExportedQuotedConnectionWithoutSplittingSemicolons()
    {
        Environment.SetEnvironmentVariable("MSSQL_CONNECTION_STRING", null);
        string connection = "Server=fixture;" + "Pass" + "word=quoted;value;Encrypt=True;";
        File.WriteAllText(Path.Combine(_root, ".env"), "# local configuration\nexport MSSQL_CONNECTION_STRING='" + connection + "'\n");

        RepoContext.LoadDotEnv(_root);

        Assert.Equal(connection, Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"));
    }

    [Fact]
    public void ExistingConnectionTakesPrecedenceWithoutReadingLocalAssignments()
    {
        Environment.SetEnvironmentVariable("MSSQL_CONNECTION_STRING", "existing-connection");
        File.WriteAllText(Path.Combine(_root, ".env"), "invalid shell command");

        RepoContext.LoadDotEnv(_root);

        Assert.Equal("existing-connection", Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"));
    }

    [Fact]
    public void FindsRepositoryFromNestedWorkingDirectory()
    {
        File.WriteAllText(Path.Combine(_root, ".release-please-manifest.json"), "{}");
        string nested = Directory.CreateDirectory(Path.Combine(_root, "nested", "working")).FullName;

        Assert.Equal(_root, RepoContext.FindRoot(nested));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MSSQL_CONNECTION_STRING", _connection);
        Directory.Delete(_root, recursive: true);
    }
}
