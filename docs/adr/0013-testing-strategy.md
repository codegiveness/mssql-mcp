# Testing strategy: unit-only in CI, integration opt-in locally

Unit tests run on every push with zero external dependencies. Integration tests live in the repo tagged `[Trait("Category", "Integration")]` and skip unless `INTEGRATION=true` env var is set. The Guard safety claim rests on unit tests; transaction rollback (ADR-0007's backstop) is verified manually before each release.

## Test split

### Unit tests (always, CI)

| Surface | Cases | How |
|---|---|---|
| Guard AST validation | ~30 (every allow/deny: SELECT, WITH...SELECT, UPDATE, INSERT, DELETE, DROP, SELECT INTO, OPENROWSET, four-part names, DECLARE, EXECUTE, etc.) | `AstValidator.Validate(sql)` pure function, no DB |
| Type coercion | ~15 (bigint→string, decimal→string, dates→ISO 8601, binary→base64, NULL→null, etc.) | Pure coercion function, no DB |
| Tool attribute wiring | ~10 (all 9 tools: `[McpServerTool]` present, `ReadOnly`/`Destructive` flags correct per mode, input schema shape) | Reflection, no DB |
| CLI arg parsing | ~5 | Parse args, verify options |
| Password obfuscation | ~3 | Regex replacement on log strings |
| Sentinel comment prefixing | ~2 | String prefix check |

Target: **~50-80 unit tests for v1**. Quality over quantity — every test proves a specific behavior.

### Integration tests (opt-in, local)

| Surface | How |
|---|---|
| `SqlExecutor` end-to-end | Real SQL Server, verify connection/timeout/retry/sentinel |
| Guard transaction rollback | INSERT in Restricted mode, verify row count 0 after |
| Discovery tools' SQL | Against real schema |
| Ops tools' DMV queries | Need real server with Query Store enabled |
| End-to-end MCP stdio roundtrip | Boot server, speak JSON-RPC over stdin/stdout pipes |

Tagged `[Trait("Category", "Integration")]`. Skipped unless `INTEGRATION=true` env var set. Require `MSSQL_CONNECTION_STRING` pointing at a real SQL Server.

## Integration DB

**Azure SQL Edge container** for local manual verification. Not in CI — arm64-incompatible, ~30s startup, ~1GB image. No cross-platform CI story justifies the flak.

## Considered Options

- **D. Skip integration tests in CI + Azure SQL Edge locally** ✅ — chosen
- A. Azure SQL Edge container in CI — rejected: arm64-incompatible, slow, flaky
- B. LocalDB — rejected: Windows-only, no cross-platform CI
- C. Real Azure SQL — rejected: cost, secrets, flakiness

## Consequences

- The Guard safety claim (ADR-0006) is fully covered by unit tests — AST allow/deny cases prove the validation logic without needing a DB.
- Transaction rollback (ADR-0007) is a backstop for AST misses, verified manually before each release. Not on every PR.
- A common failure mode is avoided: tests that require a live DB in `beforeAll`, test dead code, and have zero mocks. Our unit tests have no DB dependency and test the actual shipped code paths.
- xUnit's `[Trait]` filtering lets CI run `dotnet test --filter Category!=Integration` cleanly.
- Test project graph mirrors production: `mssql-mcp.Core.Tests` → tests Core; `mssql-mcp.Tools.Tests` → tests Tools (with Core as transitive dep).
