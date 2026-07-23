# Logging: stderr always + optional file via `MSSQL_LOG_FILE`

stderr is the primary log sink (stdout is reserved for MCP JSON-RPC frames — any byte on stdout corrupts the protocol). An optional file sink activates when `MSSQL_LOG_FILE` env var is set. Plain text format with bracketed prefix convention mirroring ADR-0010's error classes.

## Sinks

| Sink | When active | Implementation |
|---|---|---|
| stderr | Always | `builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)` — matches SDK sample, captured by all MCP hosts (Claude Desktop, Cursor) |
| File | When `MSSQL_LOG_FILE` env var is set | `Stream`-based file logger from `Microsoft.Extensions.Logging.Abstractions` — no Serilog dependency |

Both sinks share the same minimum level filter (`--log-level` flag or `MSSQL_LOG_LEVEL` env var, flag overrides env, default info).

## Format

Plain text. Structured JSON logging is a v2 concern — adds tokens, helps log aggregators (Seq, Datadog) that 99% of MCP users don't run. Consistent bracketed prefix convention mirrors ADR-0010:

`[startup]`, `[tool]`, `[guard]`, `[sql]`, `[retry]`, `[connection]`, `[internal]`

## Log events

| Event | Level |
|---|---|
| Server startup (mode, conn target, version) | Info |
| Each tool invocation (tool name, duration, row count) | Info |
| Guard rejection (which layer, why) | Info |
| SQL error (number, severity, message — not stack) | Warning |
| Transient error retry (attempt N, backoff) | Warning |
| Connection failure (retries exhausted) | Error |
| Internal exception (full stack) | Error |
| Connection string (obfuscated) | Debug |

## Password obfuscation

Single custom logger filter applies `Password=...;` → `Password=***;` regex replacement to both sinks (per ADR-0005). Applied before any formatter sees the message.

## Considered Options

- **C. stderr + optional file via env var** ✅ — chosen
- A. stderr only — rejected: no persistent audit trail for power users debugging tricky guard rejections
- B. File only — rejected: forces file management on every user; MCP hosts expect stderr
- D. stderr + always file — rejected: forces file management, surprising for a stdio tool

## Consequences

- ~~No file rotation in v1~~ — reversed by [ADR-0030](./0030-log-file-rotation.md): the file sink now performs built-in size-based rotation (`MSSQL_LOG_FILE_MAX_BYTES` / `MSSQL_LOG_FILE_MAX_ROLLS`).
- Stack traces from `[internal]` errors go to logs at `Error` level, never to the Agent (per ADR-0010).
- No new dependencies — `Microsoft.Extensions.Logging.Console` ships with the SDK; file logging uses BCL `Stream` + `Microsoft.Extensions.Logging.Abstractions`.
- Adding structured JSON logging in v2 is additive (new formatter) and non-breaking.
