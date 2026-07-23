# mssql-mcp

A Model Context Protocol (MCP) server for Microsoft SQL Server, built in C#/.NET 10. Lets AI agents (Claude Desktop, Cursor, etc.) safely query and interact with SQL Server databases through a controlled tool surface.

## Language

**Access Mode**:
The server's safety posture, selected at startup via `--access-mode`. **Restricted** = read-only with layered validation (default). **Unrestricted** = full DML/DDL access (opt-in).
_Avoid_: read-write mode, safe mode, permission level

**Restricted Mode**:
Default access mode. SQL execution is gated by the Guard: AST allowlist validation, read-only transaction enforcement, command timeout, and byte-size transport safety net. All tools carry `readOnlyHint=True`.
_Avoid_: read-only mode, safe mode

**Unrestricted Mode**:
Opt-in access mode (`--access-mode unrestricted`). Permits DML and DDL via `execute_sql` (same tool, different Guard behavior). Destructive operations carry `destructiveHint=True`.
_Avoid_: admin mode, full access mode

**Guard**:
The multi-layer safety validation applied to SQL in Restricted mode. Four layers: (1) T-SQL AST allowlist via ScriptDom (Visitor-based, iterates all batches and nested statements), (2) read-only transaction enforcement (`BEGIN TRAN ... ROLLBACK`), (3) command timeout (default 30s), (4) byte-size transport safety net (default 10 MB, truncate with notice).
_Avoid_: validator, safety filter, SQL checker

**Agent**:
The MCP client (Claude Desktop, Cursor, Windsurf, etc.) that calls the server's tools. The server never initiates communication.
_Avoid_: client, user, caller (use "Agent" for the MCP client; "end user" for the human who installed the server)

**Tool**:
A single MCP method the Agent can call. Each tool has a name, input schema (JSON Schema), and returns a `CallToolResult`. Tools are discovered by the Agent at connection time.
_Avoid_: function, method, endpoint, action

**Content**:
One item inside a `CallToolResult`. The MCP spec defines `TextContent`, `ImageContent`, `AudioContent`, and `EmbeddedResource`. We use `TextContent` exclusively in v1 — structured data is JSON-encoded inside the text.
_Avoid_: payload, response body, message

**Configuration Precedence**:
Rule for resolving runtime config values: CLI flag (if present) > env var > hardcoded default. The single exception is `MSSQL_CONNECTION_STRING`, which takes precedence over the `--connection-string` flag because secrets live in env, not argv.
_Avoid_: override chain, config hierarchy

**Onboarding Surface**:
The set of artifacts a first-time user touches to go from "heard about it" to "agent returns a query result." Four layers, in order: install one-liner, harness-specific config snippet, verify-it-works command, troubleshooting/FAQ. A repo "feels instant" when all four layers exist and the first two are copy-pasteable with zero reading.
_Avoid_: DX, user experience, getting started

**Harness**:
An MCP client application that hosts the server and drives tool calls (Claude Desktop, Cursor, Windsurf, Cline, Continue, Zed, VS Code, Codex CLI, opencode, etc.). Distinguished from the Agent (the AI) and the end user (the human). The server is harness-agnostic over stdio, but each harness has its own config file format and location.
_Avoid_: client, host, editor (use "Harness" for the MCP client app; "Agent" for the AI inside it)

**Postinstall-Independent Install**:
An install path that produces a working binary even when npm's `postinstall` lifecycle script is skipped (via `--ignore-scripts`, corporate config, or npx cache quirks). Achieved by bundling the native binary inside platform-specific npm packages declared as `optionalDependencies`, so npm itself installs the correct binary for the host platform with no lifecycle script involvement. The reference benchmark is `npx -y @colbymchenry/codegraph` working on a clean machine with only Node installed and `ignore-scripts=true`.
_Avoid_: reliable install, robust install

**Harness Verification Record**:
The per-harness artifact produced by the manual verification step (ADR-0022). For each of the 6 documented harnesses, records three things: (1) config file path per OS, (2) log file location, (3) what a successful MCP connection looks like in that harness's UI/logs. Feeds the Troubleshooting section's per-harness "where to look" table. A harness snippet cannot be published until its Verification Record is complete.
_Avoid_: harness test, client checklist
