# 08a — Structured error handling (all 6 error classes)

**What to build:** All 6 error classes implemented as structured JSON with the `error` discriminator field, replacing any placeholder error handling from earlier tickets. Every tool returns `isError: true` on failure with a JSON envelope the Agent can branch on. `GUARD_REJECTION` includes the `rule` field (e.g. `non_select_statement`, `select_into`, `openrowset`, `parse_error`, `empty_batch`). `TIMEOUT` includes `timeout_ms`. `CONNECTION` indicates retries exhausted. `SQL` includes `code`/`severity`/`line`/`procedure` for recovery logic. `INTERNAL` includes `exception_type` but never stack traces (stack goes to logs). `OBJECT_NOT_FOUND` includes the lookup parameters. Severity-25 errors are surfaced to the Agent, not fatal to the process.

**Blocked by:** 03 (`execute_sql` in Restricted mode — needs a working tool to attach error handling to)

**Status:** ready-for-agent

- [ ] `GUARD_REJECTION`: `{"error":"GUARD_REJECTION","rule":"<rule>","detail":"...","statement_type":"...","position":{"line":N,"column":N}}`
- [ ] `TIMEOUT`: `{"error":"TIMEOUT","timeout_ms":30000,"detail":"Query exceeded 30s command timeout"}`
- [ ] `CONNECTION`: `{"error":"CONNECTION","detail":"{message}. Retries exhausted."}` (only after retry logic from ticket 08d is in place — or "Retries not configured" if 08d isn't done yet)
- [ ] `SQL`: `{"error":"SQL","code":"SQL{Number}","message":"...","severity":N,"line":N,"procedure":null}`
- [ ] `INTERNAL`: `{"error":"INTERNAL","exception_type":"InvalidOperationException","detail":"{Message}"}` — NEVER stack traces
- [ ] `OBJECT_NOT_FOUND`: `{"error":"OBJECT_NOT_FOUND","schema":"...","name":"...","type":"...","database":"..."}`
- [ ] All 6 classes return `isError: true` in `CallToolResult`
- [ ] Severity-25 `SqlException` surfaced to Agent (NOT process exit)
- [ ] Empty result set returns `[]` (NOT an error) — except `get_object_details` which returns `OBJECT_NOT_FOUND`
- [ ] Stack traces go to logs at `Debug`/`Error` level (never to Agent)
- [ ] Unit tests: each error class triggered by a fake `ISqlConnection` that throws the right exception type
- [ ] Unit test: `SqlException` with severity 25 is surfaced, not fatal
