# CLAUDE.md

Project instructions for Claude Code. The shared guide for every agent lives in `AGENTS.md` and is imported below, so there is one source of truth: change project rules in `AGENTS.md`, not here.

@AGENTS.md

## Claude Code notes

- Personal or machine-specific instructions go in `CLAUDE.local.md` or `~/.claude/CLAUDE.md` (both stay out of git), never in this tracked file.
- MCP client config (`.mcp.json`, `.claude/`) can hold connection strings; it is gitignored. Keep it that way.

## Working style

These guidelines bias toward caution over speed. For trivial tasks, use judgment.

### Think before coding

- State assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them; don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.

### Simplicity first

- No features, abstractions, or configurability beyond what was asked.
- No error handling for impossible scenarios.
- If 200 lines could be 50, rewrite it.

### Surgical changes

- Don't "improve" adjacent code, comments, or formatting; match existing style.
- Remove only the imports, variables, and functions your change made unused. Mention unrelated dead code instead of deleting it.

### Goal-driven execution

Turn each task into a verifiable goal ("fix the bug" → "write a test that reproduces it, then make it pass") and loop until the check passes. For multi-step work, state a short plan with a check per step.
