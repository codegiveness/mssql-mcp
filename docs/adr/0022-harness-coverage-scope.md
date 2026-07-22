# Harness coverage scope for onboarding docs

Document config snippets for 6 harnesses: Claude Desktop, Cursor, VS Code (GitHub Copilot MCP), Windsurf, Cline/Roo Code, and Continue.dev. Docs-only — no `mssql-mcp configure <harness>` subcommand. Each snippet manually verified once before publish; not CI-tested per-harness.

## Context

Reference repos (codegraph, rtk, agentmemory) treat per-harness config snippets as table stakes — codegraph covers 8, rtk 15, agentmemory 18+. mssql-mcp currently ships only Claude Desktop snippets. The server itself is harness-agnostic over stdio, so "supporting a harness" is purely a docs problem, not a code problem.

The reference repos split on whether to also ship a config-writing command: codegraph and rtk auto-patch agent configs; agentmemory is docs-only. A config-writer adds real complexity — per-harness config-file locations (Windows vs macOS paths), existing-config-merge logic, permission prompts — for a benefit that's marginal when the user can paste a snippet in 10 seconds.

CI-testing each harness would mean spinning up each GUI app in CI, which is brittle and slow. The snippet itself is the contract; manual verification before publish is sufficient at v1 scale.

## Decision

- **6 harnesses documented**: Claude Desktop, Cursor, VS Code (GitHub Copilot MCP), Windsurf, Cline/Roo Code, Continue.dev. Covers ~90% of where SQL Server + agent users live. Zed, opencode, Codex CLI, Gemini CLI deferred — cheap to add later, but each is a maintenance and claim surface.
- **Docs-only**: one copy-paste JSON snippet per harness in a "Supported clients" table. No `mssql-mcp configure <harness>` subcommand in v1. If users ask for it, revisit.
- **Manually-verified-once**: each snippet is verified against a real client before the docs ship. No per-harness CI. The snippet is the contract.

## Considered Options

- **Must-only (3: Claude Desktop, Cursor, VS Code)** — rejected. Leaves Windsurf, Cline, Continue users to translate snippets themselves, which is exactly the friction we're removing.
- **Must+Should (6) ✅** — chosen. Covers the realistic SQL Server + agent audience without overcommitting.
- **All 10 (Must+Should+Nice)** — rejected. Each "Nice" harness (Zed, opencode, Codex CLI, Gemini CLI) is a maintenance surface and an unverified claim. Don't document what you haven't verified.
- **Docs + `configure` subcommand** — rejected for v1. Config-writer adds per-harness file-location edge cases and merge logic. Ship docs first; add the command if users ask.
- **CI-tested per harness** — rejected. Launching GUI apps in CI is brittle. Manual verification before publish is sufficient at this scale.

## Consequences

- README gains a "Supported clients" table with 6 rows, each with config file path per OS and a copy-paste JSON snippet.
- Each snippet must be manually verified against the real client before the docs are published. Unverified snippets are a claim we can't back.
- Adding a 7th+ harness later is additive and non-breaking — just a new table row.
- If user demand for `mssql-mcp configure` emerges, revisit this ADR; the command would supersede this decision for any harness it covers.
