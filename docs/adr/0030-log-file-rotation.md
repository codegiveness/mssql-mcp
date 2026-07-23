# Built-in size-based log file rotation

ADR-0011 deferred log file rotation to external tooling (`logrotate`) and documented that decision in its Consequences: *"No file rotation in v1 — users wanting rotation configure logrotate / Windows Event Tracing externally."* This ADR reverses that consequence: the file sink now rotates on its own when the configured size cap is reached, because the npm/npx distribution model means most users never set up external rotation and a long-lived MCP server (some harnesses keep the process alive across sessions) will grow the log file unbounded.

**Decision**: `FileLoggerProvider` performs size-based rotation when `MSSQL_LOG_FILE` is active.

- `MSSQL_LOG_FILE_MAX_BYTES` — byte threshold for the active log file. Default `52428800` (50 MB). When the active file reaches this size, it is closed and renamed.
- `MSSQL_LOG_FILE_MAX_ROLLS` — number of archived files to retain. Default `3`. Archived files are named `<path>.1`, `<path>.2`, `<path>.3`, oldest deleted when the count is exceeded.

Rotation is sequential rename: `<path>` → `<path>.1` → `<path>.2` → …, with the oldest (`.{max_rolls}`) deleted before the rename chain runs. No compression, no time-based rotation — size is the only trigger. The active file is always `<path>` (the value of `MSSQL_LOG_FILE`), so harnesses and log readers that tail the configured path continue to work.

stderr is unaffected — rotation applies only to the file sink.

## Considered Options

- **Built-in size-based rotation** ✅ — chosen. Self-contained, reaches the npm/npx audience that never configures logrotate, keeps the file alive (never goes dark).
- Circuit breaker (stop writing at cap, emit one "suppressed" line) — rejected: leaves the file dead after the cap, which is worse than rotation. The user thinks logging works but it doesn't.
- External tooling only (keep ADR-0011 as-is, ship a logrotate config snippet) — rejected: only reaches Linux users who run logrotate. Windows users and users who don't know where the binary lives (npx) get no protection.

## Consequences

- Supersedes ADR-0011's *"No file rotation in v1"* consequence. ADR-0011's sink/format/obfuscation decisions are unchanged.
- The `FileShare.Read` flag on the active `FileStream` means external readers hold read handles, not write handles — `File.Move` succeeds on Windows when only read handles are open, so rotation works cross-platform.
- Users who already run external `logrotate` are not harmed: if the active file has been rotated externally, the provider opens a fresh file at the configured path on the next write (append mode).
- No new dependencies — rotation is BCL file operations (`File.Move`, `File.Delete`).
- Time-based rotation and compression are explicitly out of scope; they can be added later without reversing this ADR.
