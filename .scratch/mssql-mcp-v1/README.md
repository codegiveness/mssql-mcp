# mssql-mcp v1 — Ticket Index

Spec: [`docs/SPEC-v1.md`](../../docs/SPEC-v1.md)
ADRs: [`docs/adr/`](../../docs/adr/) (16 ADRs)
Glossary: [`CONTEXT.md`](../../CONTEXT.md)

## Tickets (in dependency order)

| # | Ticket | Blocked by | What it delivers |
|---|---|---|---|
| 01 | [Scaffold + `list_databases`](issues/01-scaffold-list-databases.md) | — | First vertical slice: git, solution, 3 projects, Options, SqlExecutor, type coercion, stdio, CI, `list_databases` tool |
| 02 | [Guard: AST validation](issues/02-guard-ast-validation.md) | 01 | ScriptDom Visitor-based Guard, Layer 1+2, transaction wrapper, timeout, sentinel |
| 03 | [`execute_sql` in Restricted](issues/03-execute-sql-restricted.md) | 02 | First SQL tool, full pipeline: tool → Guard → SqlExecutor → return shape |
| 04 | [Cross-DB + discovery tools](issues/04-cross-db-discovery.md) | 01 | `list_schemas`, `list_objects`, `get_object_details`, `QuoteIdentifier`, `ValidateDatabase` |
| 05 | [`explain_query`](issues/05-explain-query.md) | 02 | Execution plan summary/XML, never executes, Guarded in both modes |
| 06 | [Ops tools](issues/06-ops-tools.md) | 04 | `analyze_indexes`, `get_top_queries`, `analyze_db_health` via DMVs |
| 07 | [Unrestricted mode](issues/07-unrestricted-mode.md) | 03 | DML/DDL via `execute_sql`, status objects, `destructiveHint=true` |
| 08a | [Structured error handling](issues/08a-error-handling.md) | 03 | 6 error classes as structured JSON with discriminator |
| 08b | [Logging](issues/08b-logging.md) | 01 | stderr + file, password obfuscation, log levels |
| 08c | [Byte-size safety net](issues/08c-byte-safety-net.md) | 03 | 10MB truncation with notice, `MSSQL_MAX_RESULT_BYTES` |
| 08d | [Retry logic](issues/08d-retry-logic.md) | 01 | `SqlRetryLogicOption`, configurable count/interval |
| 09 | [npm + release pipeline](issues/09-npm-release-pipeline.md) | 07, 08a | npm wrapper, `install.js`, `release.yml`, README |
| 10 | [`--validate` flag](issues/10-validate-flag.md) | 01 | Connection pre-flight check, exit 0/1 |

## Dependency graph

```
01 (scaffold + list_databases)
├── 02 (Guard) ───────────┬── 03 (execute_sql) ──┬── 07 (Unrestricted) ──┐
│                         │                       ├── 08a (errors) ───────┤
│                         │                       ├── 08c (byte cap)        │
│                         └── 05 (explain_query)                            │
├── 04 (cross-DB + discovery) ── 06 (ops tools)                            │
├── 08b (logging)                                                          │
├── 08d (retry)                                                            │
└── 10 (--validate)                                                        │
                                                                           │
09 (npm + release) ← 07 + 08a ────────────────────────────────────────────┘
```

## Parallelism

- After **01**: tickets **02**, **04**, **08b**, **08d**, **10** can all start in parallel (5 agents)
- After **02**: tickets **03** and **05** can start in parallel
- After **03**: tickets **07**, **08a**, **08c** can start in parallel
- After **04**: ticket **06** can start
- After **07** + **08a**: ticket **09** can start

## Frontier (tickets with no unresolved blockers)

- **01** (Scaffold + `list_databases`) — start here

## Working the tickets

Use `/implement` to work one ticket at a time, clearing context between tickets. When a ticket is done, mark its acceptance criteria as `[x]` and move to the next frontier ticket.
