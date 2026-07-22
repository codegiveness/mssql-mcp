# README restructure for onboarding-first flow

Restructure the README into 15 sections with a platform-aware quickstart at the top, a new "Supported clients" table (6 harnesses), a "Verify it works" section pointing to `--validate`, and a new "Troubleshooting" section before Security. "Why this exists" moves below the quickstart.

## Context

Current README is 267 lines, reference-doc-dense, with no "fast path." Quickstart doesn't mention `--validate`, no Troubleshooting section, no harness table, and Installation internals (platform matrix, install.js) come after the quickstart but are what a user needs if the quickstart fails — inverted priority. Reference repos (codegraph, rtk, agentmemory) all use a tiered structure: 3-step quickstart at top, reference sections below.

## Decision

### Section order (15 sections)

1. Badges + hero (add scoped npm badge)
2. **Quick start** — platform-aware: macOS/Linux `npx -y @codegiveness/mssql-mcp`, Windows `dotnet tool install -g mssql-mcp`. One Claude Desktop config snippet. One `--validate` check. Dotnet-tool-on-macOS/Linux in a `<details>`.
3. **Supported clients** (NEW) — 6-harness table (Claude Desktop, Cursor, VS Code/Copilot, Windsurf, Cline/Roo, Continue), one snippet per row, less-common collapsed.
4. **Verify it works** (NEW) — `--validate` as proof command; points to Troubleshooting if it passes but agent can't see server.
5. Access modes
6. Tools
7. Authentication
8. Configuration
9. Installation (platform matrix + install.js internals) — demoted to reference
10. **Troubleshooting** (NEW) — per-harness log locations, common wiring failures, "agent can't see server" diagnosis flow
11. Security
12. Development
13. Trademarks & licensing
14. Contributing
15. Stability

### Sub-decisions

- **Platform-aware quickstart**: two one-liners at the top, OS-labeled (macOS/Linux vs Windows), no tabs or collapsibles. A Windows user shouldn't read fine print to find their path.
- **"Why this exists"** moves below the Quick start (becomes context for the curious, not a gate on install).

## Consequences

- README grows by ~3 sections (Supported clients, Verify it works, Troubleshooting) — estimated 350-400 lines from current 267.
- Every future onboarding change lands in one of these 15 sections — no ad-hoc additions.
- Platform-aware quickstart means the npm badge and the quickstart snippet both point to `@codegiveness/mssql-mcp` (ADR-0023).
- Installation internals (platform matrix, install.js behavior) stay documented but are clearly reference material, not quickstart.
