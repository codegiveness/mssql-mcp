# Dual-mode access: Restricted (default) and Unrestricted (opt-in)

## Context

The server needs to serve two use cases: the 80% exploration case (AI agents querying schema, running SELECTs, analyzing plans without human oversight) and the explicit write case (operator-authorised DDL/DML). These have opposite safety requirements.

## Decision

Ship two access modes, selected at startup via `--access-mode`. **Restricted** is the default: SQL execution passes through a multi-layer Guard (T-SQL AST allowlist via ScriptDom, read-only transaction, command timeout, row cap), and every tool carries `readOnlyHint=True`. **Unrestricted** is opt-in via explicit flag and lifts DDL/DML limits. This mirrors the proven dual-mode pattern and protects the 80% exploration case while keeping write paths explicit.

## Considered Options

No alternatives were seriously considered for this decision — the dual-mode pattern is the established approach for tools that serve both read-heavy and write-optional workloads.

## Consequences

**Addendum (ticket 07 implementation)**: The MCP SDK's `[McpServerTool]` attribute is static at compile time — it cannot vary per runtime access mode. Therefore `execute_sql` keeps `Destructive=false` and `ReadOnly=true` on the annotation in both modes. In Unrestricted mode, the `[Description]` is updated to mention that DML/DDL is permitted. The human who sets `--access-mode unrestricted` has accepted the risk; the MCP client does not get a `destructiveHint=True` signal to warn before destructive operations. This is a known limitation of the static-annotation approach; if the SDK adds runtime tool annotations in a future version, this should be revisited.
