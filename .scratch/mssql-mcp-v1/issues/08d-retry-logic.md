# 08d — Transient failure retry (SqlRetryLogicOption)

**What to build:** Transient connection and command failures are retried automatically using Microsoft's `SqlRetryLogicOption` (Microsoft maintains the transient-error list — do NOT write a custom retry layer or a custom `isTransientError()` function). Defaults: retry count 3, backoff range 2-10 seconds. Both configurable via env vars `MSSQL_RETRY_COUNT` and `MSSQL_RETRY_INTERVAL`. Retries are transparent to the Agent — only when retries are exhausted does a `CONNECTION` error surface. This is the c0h1b4 failure mode we're fixing (they wrote `isTransientError()` but never wired it up).

**Blocked by:** 01 (Scaffold — needs `SqlExecutor` to attach retry logic to)

**Status:** ready-for-agent

- [ ] `SqlRetryLogicOption` configured with transient error detection from Microsoft.Data.SqlClient
- [ ] Retry count: default 3, overridable via `MSSQL_RETRY_COUNT`
- [ ] Backoff: default 2-10 seconds, overridable via `MSSQL_RETRY_INTERVAL` (min-max format or single value)
- [ ] Retry logic attached to both `SqlConnection` (open) and `SqlCommand` (execute)
- [ ] On retry: log `[retry] Transient error {Number}, attempt {N} of {Max}...` at `info` level
- [ ] On exhaustion: surface as `CONNECTION` error with "Retries exhausted" detail
- [ ] No custom `isTransientError()` function — rely entirely on Microsoft's built-in transient error list
- [ ] Invalid `MSSQL_RETRY_COUNT` (negative, non-numeric) fails fast at startup
- [ ] Invalid `MSSQL_RETRY_INTERVAL` fails fast at startup
- [ ] Unit test: fake `ISqlConnection` throws transient error twice then succeeds; verify 3 attempts total, success on 3rd
- [ ] Unit test: fake `ISqlConnection` throws transient error 4 times; verify `CONNECTION` error with "Retries exhausted"
- [ ] Unit test: non-transient error (e.g. `SqlException` severity 16, syntax error) does NOT trigger retry
