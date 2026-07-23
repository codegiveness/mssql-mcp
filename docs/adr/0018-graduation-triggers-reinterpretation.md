# 1.0.0 graduation triggers reinterpretation

Ship `0.1.0` first (private repo, full pipeline test), then reinterpret the five graduation triggers from ADR-0014 with concrete satisfaction criteria per trigger, then tag `1.0.0-rc.1` → 7-day promotion gate → `1.0.0`. **Supersedes** ADR-0014 §"`0.1.0` → `1.0.0` graduation triggers" and §"Release candidates" ONLY; ADR-0014 body stays intact.

## Context

ADR-0014 defined five graduation triggers for `0.1.0` → `1.0.0` but left them abstract ("public security review", "one full minor release cycle"). With `0.1.0` about to ship, each trigger needs a concrete satisfaction criterion and an owner. The original triggers also assumed a linear `0.1.0` → `0.2.0` → … → `1.0.0` path; Path B (ship `0.1.0`, dogfood in parallel with security research, skip intermediate minors if stable, go straight to `1.0.0-rc.1`) is faster and still safe because the tool surface is stable from `0.1.0` (ADR-0014 §"Dual stability contract").

## Decision

### Path B: ship 0.1.0 first, then rc → 1.0.0

1. Tag `v0.1.0` against the private repo, verify the full release pipeline end-to-end (build, 5 RIDs, archives, checksums, GitHub Release, NuGet Trusted Publishing, npm with provenance), verify all 5 RID installations, then flip the repo public (issue #19).
2. Dogfood 30 days (#20) and run `/security-research` skill (#21) **in parallel** — they're independent.
3. If both pass: tag `v1.0.0-rc.1`, wait 7 days, promote to `v1.0.0` (#22).

### Per-trigger resolutions

| # | ADR-0014 trigger | Reinterpretation | Owner | Ticket |
|---|---|---|---|---|
| 1 | Production usage ≥ 30 days, no data loss | Maintainer dogfoods `0.1.0` daily against a production SQL Server for 30 consecutive days, logging each day's usage. Literal compliance — ADR-0014 explicitly says "maintainer or external user." | Maintainer (HITL) | #20 |
| 2 | Guard survived public security review | `/security-research` skill (3 vulnerability hunters + 2 PoC engineers) audits `SqlGuard.cs` + `GuardTests.cs` against ADR-0006 vectors. Findings published as `docs/security-audits/`. "Public" = methodology transparent + findings published, not "a human researcher posted a public report." Critical/High bypasses fixed before `1.0.0-rc.1`. | Agent | #21 |
| 3 | Tool surface stable for one full minor cycle | `0.1.0` → `1.0.0` interval IS the stable cycle. Tool surface was frozen at `0.1.0` (ADR-0014 §"Dual stability contract": tool names, params, types don't break within `0.x`). No intermediate minor needed if no new tools are added. | N/A (structural) | — |
| 4 | Distribution verified on all 5 RIDs | Verified during `0.1.0` release (#19): `npm install -g mssql-mcp` + `dotnet tool install -g codegiveness.mssql-mcp` on linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64. Each must launch and print `0.1.0`. | Maintainer (HITL) | #19 |
| 5 | Test coverage ≥ 80 unit tests, ≥ 30 Guard AST cases | Already met: 310 tests pass (152 Core + 158 Tools), 31 Guard AST `Reject_*` cases (ADR-0006 vectors covered). | Done | #13 |

### `1.0.0-rc.1` → `1.0.0` promotion gate

A `1.0.0-rc.1` issue blocks promotion to `1.0.0` **iff** it is labeled `bug` AND falls into one of:
- Data loss or corruption in any mode
- Guard bypass (any AST attack vector that should be blocked but isn't)
- Distribution failure on any of the 5 RIDs
- Crash on any standard tool invocation in Restricted mode

Everything else ships as a known issue in the `1.0.0` release notes, targeted for `1.0.1`.

**7-day clock:** starts when `v1.0.0-rc.1` is tagged and the GitHub Release is published. If a blocking issue is found and fixed, tag `v1.0.0-rc.2` and restart the clock. Non-blocking issues do not restart the clock.

## Considered Options

- **A. Linear minors (0.1.0 → 0.2.0 → 0.3.0 → 1.0.0)** — rejected. Adds 2+ months of calendar time for no safety gain when the tool surface is already frozen at `0.1.0` and dogfooding + security research run in parallel.
- **B. Ship 0.1.0, dogfood + security research in parallel, rc → 1.0.0** ✅ — chosen. Fastest safe path: the two long-pole triggers (30-day dogfood, security review) run concurrently rather than serially.
- **C. Skip rc, go 0.1.0 → 1.0.0 directly** — rejected. The 7-day rc gate catches distribution or regression issues that dogfooding on a single machine can't surface (e.g. a RID-specific failure that only manifests on a different OS).

## Consequences

- `0.1.0` ships to a private repo first — the release pipeline is tested before public exposure.
- The public flip (#19) is the last step of `0.1.0`, not the first step of `1.0.0`.
- Dogfooding (#20) and security research (#21) run in parallel against the public `0.1.0` — external early adopters can participate during this window.
- `NUGET_API_KEY` is deleted from repo secrets only after the first successful Trusted Publishing push (verified in #19).
- If the 7-day rc gate finds a blocking issue, the clock restarts with `rc.2`. There is no limit on rc iterations, but each rc must fix at least one blocking issue (no speculative re-tags).
- ADR-0014 §"graduation triggers" and §"release candidates" are superseded by this ADR. ADR-0014 §"Dual stability contract" (tool surface stable from `0.1.0`) stands — this ADR depends on it.

## Status

Accepted (2026-07-21). Supersedes ADR-0014 §"`0.1.0` → `1.0.0` graduation triggers" and §"Release candidates" only.
