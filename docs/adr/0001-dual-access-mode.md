# Dual-mode access: Restricted (default) and Unrestricted (opt-in)

Ship two access modes, selected at startup via `--access-mode`. **Restricted** is the default: SQL execution passes through a multi-layer Guard (T-SQL AST allowlist via ScriptDom, read-only transaction, command timeout, row cap), and every tool carries `readOnlyHint=True`. **Unrestricted** is opt-in via explicit flag and lifts DDL/DML limits with `destructiveHint=True` annotations on mutating tools. This mirrors the proven postgres-mcp shape and protects the 80% exploration case while keeping write paths explicit.
