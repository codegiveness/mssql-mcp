using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;
using SharpFuzz;

namespace MssqlMcp.Fuzz;

internal static class Program
{
    private static readonly SqlGuard Guard = new(
        new MssqlMcpOptions { AccessMode = AccessMode.Restricted },
        NullLogger<SqlGuard>.Instance);
    private static readonly WritesForbiddenVisitor NoWrites = new();

    public static void Main()
    {
        // Verify the native bridge detects a managed exception in a separate probe run.
        // The campaign never sets this flag; the exception is not a production finding.
        bool crashProbe = string.Equals(
            Environment.GetEnvironmentVariable("MSSQL_FUZZ_CRASH_PROBE"), "1", StringComparison.Ordinal);
        Fuzzer.LibFuzzer.Run(input =>
        {
            if (crashProbe)
            {
                throw new InvalidOperationException("Intentional fuzz-engine crash-detection probe.");
            }
            Exercise(input);
        });
    }

    private static void Exercise(ReadOnlySpan<byte> input)
    {
        string sql = Encoding.UTF8.GetString(input);
        TSql160Parser parser = new(initialQuotedIdentifiers: false);
        using StringReader reader = new(sql);
        // Parse directly first: Guard catches parser exceptions, which would otherwise
        // hide genuine unexpected parser failures from the fuzz engine.
        TSqlFragment fragment = parser.Parse(reader, out var errors);
        GuardResult result = Guard.ValidateStrict(sql);
        if (!result.Accepted)
        {
            return;
        }

        if (errors.Count != 0 || fragment is not TSqlScript script)
        {
            throw new InvalidOperationException("Guard accepted malformed SQL.");
        }
        bool sawStatement = false;
        foreach (TSqlBatch batch in script.Batches)
        {
            sawStatement |= batch.Statements.Count != 0;
        }
        if (!sawStatement)
        {
            throw new InvalidOperationException("Guard accepted SQL with no executable statement.");
        }
        script.Accept(NoWrites);
    }

    private sealed class WritesForbiddenVisitor : TSqlFragmentVisitor
    {
        public override void Visit(TSqlStatement node)
        {
            if (node is not SelectStatement)
            {
                throw new InvalidOperationException("Guard accepted a non-SELECT statement.");
            }
        }

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
            {
                throw new InvalidOperationException("Guard accepted SELECT INTO.");
            }
        }

        public override void Visit(NextValueForExpression node)
        {
            throw new InvalidOperationException("Guard accepted sequence allocation via NEXT VALUE FOR.");
        }
    }
}
