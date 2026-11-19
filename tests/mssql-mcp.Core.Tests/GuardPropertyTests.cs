using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using mssql_mcp.Core.Configuration;
using mssql_mcp.Core.Guard;

namespace mssql_mcp.Core.Tests;

/// <summary>
/// FsCheck property-based tests for the Restricted-mode Guard (ADR-0006). The example-based matrix in
/// <see cref="GuardTests"/> pins known attack vectors; these properties generate thousands of
/// structurally different inputs around the same rules: read-only shapes must always be accepted,
/// write/DDL statements must be rejected wherever they hide, and the Guard must never throw.
/// </summary>
/// <remarks>
/// Every property runs with a fixed replay seed so CI is deterministic and a failure reproduces
/// bit-for-bit; FsCheck prints the failing input and its replay string. To explore new inputs locally,
/// change <see cref="Seed"/> (format <c>seed,gamma</c>, gamma odd). Size is bounded so the whole class
/// stays in the low seconds.
/// </remarks>
public class GuardPropertyTests
{
    private const int Runs = 150;
    private const int MaxSize = 40;
    private const string Seed = "20260719,7919";

    private static readonly SqlGuard RestrictedGuard = CreateGuard(AccessMode.Restricted);
    private static readonly SqlGuard UnrestrictedGuard = CreateGuard(AccessMode.Unrestricted);

    /// <summary>
    /// Keyword tokens that must never survive in an accepted Restricted-mode batch. MERGE is omitted on
    /// purpose: <c>OPTION (MERGE JOIN)</c> is a legal query hint inside a SELECT.
    /// </summary>
    private static readonly HashSet<TSqlTokenType> ForbiddenTokens =
    [
        TSqlTokenType.Insert,
        TSqlTokenType.Update,
        TSqlTokenType.Delete,
        TSqlTokenType.Drop,
        TSqlTokenType.Alter,
        TSqlTokenType.Create,
        TSqlTokenType.Truncate,
        TSqlTokenType.Exec,
        TSqlTokenType.Execute,
        TSqlTokenType.Into,
    ];

    private static SqlGuard CreateGuard(AccessMode mode) => new(
        new MssqlMcpOptions { ConnectionString = "Server=localhost;", AccessMode = mode },
        NullLogger<SqlGuard>.Instance);

    private static string Describe(string sql, GuardResult result) =>
        result.Accepted
            ? $"Guard accepted: {sql}"
            : $"Guard rejected [{result.Rejection?.Rule}] {result.Rejection?.Detail} :: {sql}";

    private static void AssertWellFormed(string sql, GuardResult result)
    {
        if (result.Accepted)
        {
            Assert.NotNull(result.WrappedSql);
            Assert.Null(result.Rejection);
        }
        else
        {
            Assert.Null(result.WrappedSql);
            Assert.NotNull(result.Rejection);
            Assert.False(string.IsNullOrEmpty(result.Rejection.Rule), $"Rejection without a rule :: {sql}");
            Assert.False(string.IsNullOrEmpty(result.Rejection.Detail), $"Rejection without detail :: {sql}");
        }
    }

    private static bool ContainsForbiddenToken(string sql)
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: false);
        using StringReader reader = new(sql);
        IList<TSqlParserToken> tokens = parser.GetTokenStream(reader, out _);
        return tokens.Any(token => ForbiddenTokens.Contains(token.TokenType));
    }

    // ---------- Accept: read-only shapes ----------

    /// <summary>Every generated SELECT / WITH...SELECT / UNION / join / subquery shape is accepted and wrapped.</summary>
    [Property(MaxTest = Runs, EndSize = MaxSize, Replay = Seed)]
    public Property ReadOnlyQueryShapes_AreAccepted() =>
        Prop.ForAll(Arb.From(SqlGenerators.ReadOnlyQuery), sql =>
        {
            GuardResult result = RestrictedGuard.Validate(sql);

            Assert.True(result.Accepted, Describe(sql, result));
            AssertWellFormed(sql, result);
            Assert.StartsWith("/* mssql-mcp */", result.WrappedSql, StringComparison.Ordinal);
            Assert.EndsWith("ROLLBACK TRANSACTION", result.WrappedSql, StringComparison.Ordinal);
            Assert.Contains(sql, result.WrappedSql, StringComparison.Ordinal);
            return true;
        });

    /// <summary>
    /// Write/DDL keywords inside comments, string literals and quoted identifiers are data, not
    /// statements: a SELECT carrying them as decoys must still be accepted.
    /// </summary>
    [Property(MaxTest = Runs, EndSize = MaxSize, Replay = Seed)]
    public Property ForbiddenKeywordsInCommentsStringsAndIdentifiers_DoNotCauseRejection() =>
        Prop.ForAll(Arb.From(SqlGenerators.QueryWithInertKeywords), sql =>
        {
            GuardResult result = RestrictedGuard.Validate(sql);

            Assert.True(result.Accepted, Describe(sql, result));
            return true;
        });

    // ---------- Reject: write / DDL / EXEC / SELECT INTO ----------

    /// <summary>
    /// INSERT/UPDATE/DELETE/MERGE/TRUNCATE/DROP/ALTER/CREATE/EXEC/SELECT INTO and friends are rejected
    /// alone, after or before a harmless SELECT, inside BEGIN/IF/WHILE/TRY blocks, and behind comments.
    /// A <c>parse_error</c> would mean the generator, not the Guard, is wrong, so it fails the property.
    /// </summary>
    [Property(MaxTest = Runs, EndSize = MaxSize, Replay = Seed)]
    public Property ForbiddenStatements_AreRejected_InAnyBatchContext() =>
        Prop.ForAll(Arb.From(SqlGenerators.ForbiddenStatementInContext), sql =>
        {
            GuardResult result = RestrictedGuard.Validate(sql);

            Assert.False(result.Accepted, Describe(sql, result));
            AssertWellFormed(sql, result);
            Assert.NotEqual("parse_error", result.Rejection?.Rule);
            Assert.NotEqual("empty_batch", result.Rejection?.Rule);
            return true;
        });

    /// <summary>
    /// The ADR-0006 layer-2 blocklist (OPENROWSET/OPENQUERY/NEXT VALUE FOR/four-part names/...) is
    /// enforced with the expected rule even when nested inside CTEs, derived tables, EXISTS and UNION.
    /// </summary>
    [Property(MaxTest = Runs, EndSize = MaxSize, Replay = Seed)]
    public Property BlockedSelectConstructs_AreRejected_WithTheirRule_WhenNested() =>
        Prop.ForAll(Arb.From(SqlGenerators.BlockedConstructInContext), case_ =>
        {
            GuardResult result = RestrictedGuard.Validate(case_.Sql);

            Assert.False(result.Accepted, Describe(case_.Sql, result));
            Assert.Equal(case_.ExpectedRule, result.Rejection?.Rule);
            return true;
        });

    // ---------- Totality: never throws ----------

    /// <summary>
    /// Arbitrary strings (random UTF-16 including lone surrogates, punctuation soup, SQL token soup)
    /// make both modes return a well-formed accept or reject; no input may throw.
    /// </summary>
    [Property(MaxTest = Runs, EndSize = MaxSize, Replay = Seed)]
    public Property Validate_NeverThrows_OnArbitraryStrings() =>
        Prop.ForAll(Arb.From(SqlGenerators.AnyString), sql =>
        {
            foreach (SqlGuard guard in new[] { RestrictedGuard, UnrestrictedGuard })
            {
                AssertWellFormed(sql, guard.Validate(sql));
                AssertWellFormed(sql, guard.ValidateStrict(sql));
            }

            return true;
        });

    /// <summary>
    /// Soundness oracle: for valid queries mutated by token insertion, span deletion/duplication and
    /// spliced-in write statements (many still parse), an accepted Restricted batch contains none of the
    /// forbidden statement keywords, as seen by ScriptDom's own lexer.
    /// </summary>
    [Property(MaxTest = Runs, EndSize = MaxSize, Replay = Seed)]
    public Property AcceptedBatches_NeverContainForbiddenKeywordTokens() =>
        Prop.ForAll(Arb.From(SqlGenerators.MutatedQuery), sql =>
        {
            GuardResult result = RestrictedGuard.Validate(sql);

            Assert.False(result.Accepted && ContainsForbiddenToken(sql), Describe(sql, result));
            return true;
        });

    // ---------- Mode semantics ----------

    /// <summary>
    /// Restricted <c>Validate</c> equals <c>ValidateStrict</c>; <c>ValidateStrict</c> ignores the access mode;
    /// Unrestricted <c>Validate</c> passes any non-blank input through verbatim (ADR-0006).
    /// </summary>
    [Property(MaxTest = Runs, EndSize = MaxSize, Replay = Seed)]
    public Property AccessMode_OnlyAffectsValidate_NotValidateStrict() =>
        Prop.ForAll(Arb.From(SqlGenerators.AnyInput), sql =>
        {
            GuardResult strict = RestrictedGuard.ValidateStrict(sql);

            Assert.Equal(strict, RestrictedGuard.Validate(sql));
            Assert.Equal(strict, UnrestrictedGuard.ValidateStrict(sql));
            if (!string.IsNullOrWhiteSpace(sql))
            {
                Assert.Equal(GuardResult.Accept(sql), UnrestrictedGuard.Validate(sql));
            }

            return true;
        });
}

/// <summary>
/// One layer-2 blocklist probe: the SQL and the Guard rule that must reject it.
/// </summary>
public sealed record BlockedConstructCase(string Sql, string ExpectedRule);

/// <summary>
/// FsCheck generators for T-SQL text. All generators are bounded: no recursion, fixed vocabulary
/// sizes, so each case parses in well under a millisecond once the parser is warm.
/// </summary>
public static class SqlGenerators
{
    private static readonly string[] Tables =
        ["Users", "Orders", "dbo.Products", "sys.databases", "[Line Items]", "HR.Employees"];

    private static readonly string[] Columns = ["id", "name", "total", "[Order Date]", "created_at"];

    // Pre-escaped T-SQL string-literal bodies; several look like attacks on purpose.
    private static readonly string[] LiteralFragments =
    [
        "abc", "x y", "it''s", "50%", "DROP TABLE Users", "; DELETE FROM Users;", "--", "/*", "*/",
        "INSERT INTO Users VALUES (1)", "EXEC sp_who", "SELECT 1 INTO #t",
    ];

    private static readonly string[] WriteStatements =
    [
        "INSERT INTO Users (id, name) VALUES (1, 'a')",
        "INSERT Users SELECT * FROM Orders",
        "UPDATE Users SET name = 'x' WHERE id = 1",
        "DELETE FROM Users WHERE id = 1",
        "DELETE Users",
        "TRUNCATE TABLE Users",
        "MERGE Users AS t USING Orders AS s ON t.id = s.id WHEN MATCHED THEN DELETE;",
        "DROP TABLE Users",
        "DROP VIEW v1",
        "ALTER TABLE Users ADD c int",
        "CREATE TABLE t1 (id int)",
        "CREATE INDEX ix1 ON Users (id)",
        "EXEC sp_who",
        "EXECUTE sp_helpdb 'master'",
        "EXEC ('DROP TABLE Users')",
        "SELECT id INTO #copy FROM Users",
        "SELECT * INTO dbo.Snapshots FROM Orders WHERE id > 1",
        "WITH c AS (SELECT 1 AS id) DELETE FROM Users WHERE id IN (SELECT id FROM c)",
        "WITH c AS (SELECT 1 AS id) INSERT INTO Users (id) SELECT id FROM c",
        "WITH c AS (SELECT 1 AS id) UPDATE Users SET name = 'x' WHERE id IN (SELECT id FROM c)",
        "GRANT SELECT ON Users TO public",
        "USE master",
        "DECLARE @x int = 1",
        "WAITFOR DELAY '00:00:01'",
    ];

    private static readonly BlockedConstructCase[] BlockedConstructs =
    [
        new("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=local;', 'SELECT 1')", "openrowset"),
        new("SELECT * FROM OPENROWSET(BULK 'C:\\file.txt', SINGLE_CLOB) AS x", "openrowset_bulk"),
        new("SELECT * FROM OPENQUERY(link, 'SELECT 1')", "openquery"),
        new("SELECT NEXT VALUE FOR dbo.Seq", "next_value_for"),
        new("SELECT * FROM srv.db.dbo.Users", "four_part_name"),
        new("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=local;').Northwind.dbo.Employees", "opendatasource"),
    ];

    private static readonly string[] Vocabulary =
    [
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "CREATE",
        "EXEC", "EXECUTE", "TRUNCATE", "TABLE", "AS", "WITH", "UNION", "ALL", "BEGIN", "END", "IF", "GO",
        "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "NEXT VALUE FOR", "BULK", "t", "a.b.c.d", "@x", "1",
        "*", "=", "(", ")", ";", ",", "'", "''", "/*", "*/", "--", "\n", "[", "]",
    ];

    private static readonly char[] PunctuationAlphabet =
        [.. "SELECTFROMWHERE1 ;'\"-/*()[]@#\n\t.,=%INSERTDROP"];

    private static readonly Func<string, string>[] Decorations =
    [
        sql => sql,
        sql => $"{sql};",
        sql => $"  {sql}  ",
        sql => $"\n{sql}\n",
        sql => $"/* header */ {sql}",
        sql => $"{sql} -- trailing",
        sql => $"-- first line\n{sql}",
        sql => $"{sql}; -- done",
    ];

    private static readonly Func<string, string>[] StatementContexts =
    [
        statement => statement,
        statement => $"SELECT 1; {statement}",
        statement => $"{Terminate(statement)} SELECT 1",
        statement => $"SELECT 1; {Terminate(statement)} SELECT 2",
        statement => $"BEGIN {statement} END",
        statement => $"IF 1 = 1 {statement}",
        statement => $"IF 1 = 0 SELECT 1 ELSE {statement}",
        statement => $"WHILE 1 = 0 BEGIN {statement} END",
        statement => $"BEGIN TRY {statement} END TRY BEGIN CATCH SELECT 1 END CATCH",
        statement => $"/* leading comment */ {statement}",
        statement => $"SELECT 1 /* gap */; {statement} -- trailing",
    ];

    // MERGE must carry its own terminator; every other template gets one only when more tokens follow.
    private static string Terminate(string statement) =>
        statement.EndsWith(';') ? statement : $"{statement};";

    private static readonly Func<string, string>[] SelectNestings =
    [
        select => select,
        select => $"WITH cte1 AS ({select}) SELECT * FROM cte1",
        select => $"SELECT * FROM ({select}) AS d",
        select => $"SELECT 1 WHERE EXISTS ({select})",
        select => $"{select} UNION ALL SELECT 1",
        select => $"SELECT 1 UNION ALL {select}",
    ];

    private static readonly Gen<string> Column = Gen.Elements(Columns);

    private static readonly Gen<string> Table = Gen.Elements(Tables);

    private static readonly Gen<string> IntLiteral =
        Gen.Choose(-1000, 1000).Select(n => n.ToString(CultureInfo.InvariantCulture));

    private static readonly Gen<string> StringLiteral =
        from count in Gen.Choose(0, 3)
        from fragments in Gen.ArrayOf(Gen.Elements(LiteralFragments), count)
        select $"'{string.Concat(fragments)}'";

    private static readonly Gen<string> Scalar = Gen.OneOf(Column, IntLiteral, StringLiteral);

    private static readonly Gen<string> Expression = Gen.OneOf(
        Scalar,
        Gen.Constant("GETDATE()"),
        Gen.Constant("COUNT(*)"),
        from c in Column select $"COALESCE({c}, 0)",
        from c in Column select $"CAST({c} AS varchar(20))",
        from c in Column select $"CASE WHEN {c} > 1 THEN 'hi' ELSE 'lo' END",
        from c in Column from n in IntLiteral select $"{c} + {n}");

    private static readonly Gen<string> SimplePredicate = Gen.OneOf(
        from c in Column from v in Scalar select $"{c} = {v}",
        from c in Column from n in IntLiteral select $"{c} > {n}",
        from c in Column select $"{c} IS NOT NULL",
        from c in Column select $"{c} LIKE 'a%'",
        from c in Column from a in IntLiteral from b in IntLiteral select $"{c} BETWEEN {a} AND {b}",
        from c in Column from a in IntLiteral from b in IntLiteral select $"{c} IN ({a}, {b})");

    private static readonly Gen<string> Predicate = Gen.OneOf(
        SimplePredicate,
        from a in SimplePredicate from b in SimplePredicate select $"{a} AND {b}",
        from a in SimplePredicate from b in SimplePredicate select $"({a}) OR NOT ({b})");

    private static readonly Gen<string> WhereClause = Gen.OneOf(
        Gen.Constant(string.Empty),
        from p in Predicate select $" WHERE {p}");

    private static readonly Gen<string> OrderByClause = Gen.OneOf(
        Gen.Constant(string.Empty),
        from c in Column from direction in Gen.Elements("ASC", "DESC") select $" ORDER BY {c} {direction}");

    private static readonly Gen<string> SelectList = Gen.OneOf(
        Gen.Constant("*"),
        Expression,
        from a in Expression from b in Expression select $"{a}, {b}");

    private static readonly Gen<string> Top = Gen.OneOf(
        Gen.Constant(string.Empty),
        from n in Gen.Choose(1, 100) select $"TOP {n} ");

    private static readonly Gen<string> FlatSelect =
        from top in Top
        from list in SelectList
        from table in Table
        from filter in WhereClause
        select $"SELECT {top}{list} FROM {table}{filter}";

    private static readonly Gen<string> SetOperator = Gen.Elements("UNION", "UNION ALL", "EXCEPT", "INTERSECT");

    private static readonly Gen<string> QueryShape = Gen.OneOf(
        from q in FlatSelect from order in OrderByClause select q + order,
        from c in Column
        from t1 in Table
        from t2 in Table
        select $"SELECT a.{c}, b.{c} FROM {t1} AS a INNER JOIN {t2} AS b ON a.{c} = b.{c}",
        from c in Column
        from t in Table
        from n in IntLiteral
        select $"SELECT {c}, COUNT(*) FROM {t} GROUP BY {c} HAVING COUNT(*) > {n}",
        from c in Column
        from t in Table
        from inner in FlatSelect
        select $"SELECT * FROM {t} WHERE {c} IN ({inner})",
        from inner in FlatSelect select $"SELECT * FROM ({inner}) AS d",
        from inner in FlatSelect select $"WITH cte1 AS ({inner}) SELECT * FROM cte1",
        from a in FlatSelect
        from b in FlatSelect
        select $"WITH cte1 AS ({a}), cte2 AS ({b}) SELECT * FROM cte1, cte2",
        from a in FlatSelect from op in SetOperator from b in FlatSelect select $"{a} {op} {b}");

    /// <summary>Syntactically valid read-only queries in many shapes, optionally with comments and a trailing semicolon.</summary>
    public static Gen<string> ReadOnlyQuery { get; } =
        from query in QueryShape
        from decorate in Gen.Elements(Decorations)
        select decorate(query);

    /// <summary>Read-only queries that carry forbidden statements as comment, string-literal or identifier decoys.</summary>
    public static Gen<string> QueryWithInertKeywords { get; } =
        from decoy in Gen.Elements(WriteStatements)
        from query in FlatSelect
        from carrier in Gen.Elements<Func<string, string, string>>(
            (q, d) => $"/* {d} */ {q}",
            (q, d) => $"{q} /* {d} */",
            (q, d) => $"-- {d}\n{q}",
            (q, d) => $"{q} -- {d}",
            (q, d) => $"{q} /* {d} */ -- {d}\n",
            (q, d) => $"SELECT '{d.Replace("'", "''", StringComparison.Ordinal)}' AS note, * FROM ({q}) AS x",
            (q, d) => $"SELECT 1 AS [{d}] FROM ({q}) AS x")
        select carrier(query, decoy);

    /// <summary>A write/DDL/EXEC/SELECT INTO statement embedded in a batch, block or comment context.</summary>
    public static Gen<string> ForbiddenStatementInContext { get; } =
        from statement in Gen.Elements(WriteStatements)
        from context in Gen.Elements(StatementContexts)
        select context(statement);

    /// <summary>A layer-2 blocked construct (and its expected rule) nested inside an otherwise valid SELECT.</summary>
    public static Gen<BlockedConstructCase> BlockedConstructInContext { get; } =
        from blocked in Gen.Elements(BlockedConstructs)
        from nest in Gen.Elements(SelectNestings)
        select blocked with { Sql = nest(blocked.Sql) };

    /// <summary>Space-joined tokens from a SQL-flavoured vocabulary; frequently almost valid, rarely valid.</summary>
    public static Gen<string> TokenSoup { get; } =
        from count in Gen.Choose(1, 25)
        from tokens in Gen.ArrayOf(Gen.Elements(Vocabulary), count)
        from separator in Gen.Elements(" ", " ", " ", string.Empty)
        select string.Join(separator, tokens);

    private static Gen<string> Mutate(string sql) =>
        from position in Gen.Choose(0, sql.Length)
        from span in Gen.Choose(0, Math.Min(8, sql.Length - position))
        from token in Gen.Elements(Vocabulary)
        from statement in Gen.Elements(WriteStatements)
        from kind in Gen.Choose(0, 3)
        select kind switch
        {
            0 => sql.Insert(position, $" {token} "),
            1 => sql.Remove(position, span),
            2 => sql.Insert(position, sql[position..(position + span)]),
            _ => sql.Insert(position, $" {statement} "),
        };

    /// <summary>Valid read-only queries after one or two random mutations; most break, some stay valid.</summary>
    public static Gen<string> MutatedQuery { get; } =
        from query in ReadOnlyQuery
        from once in Mutate(query)
        from twice in Gen.OneOf(Gen.Constant(once), Mutate(once))
        select twice;

    private static readonly Gen<string> RandomUtf16 =
        from count in Gen.Choose(0, 60)
        from chars in Gen.ArrayOf(Gen.Choose(0, 0xFFFF).Select(code => (char)code), count)
        select new string(chars);

    private static readonly Gen<string> PunctuationSoup =
        from count in Gen.Choose(0, 60)
        from chars in Gen.ArrayOf(Gen.Elements(PunctuationAlphabet), count)
        select new string(chars);

    /// <summary>Random UTF-16, punctuation soup, token soup, mutated queries and blank strings.</summary>
    public static Gen<string> AnyString { get; } = Gen.OneOf(
        RandomUtf16,
        PunctuationSoup,
        TokenSoup,
        MutatedQuery,
        Gen.Elements(string.Empty, "   ", "\t\r\n", "--", "/**/"));

    /// <summary><see cref="AnyString"/> mixed with valid read-only queries and forbidden statements.</summary>
    public static Gen<string> AnyInput { get; } = Gen.OneOf(
        AnyString,
        ReadOnlyQuery,
        ForbiddenStatementInContext);
}
