# Phased release: 0.1.1 typo fix, 0.2.0 code+CI, 0.3.0 docs+package

Ship the onboarding surface in three phases. **0.1.1** is an immediate doc-only patch fixing the `npx -y mssql-mcp` typo (wrong npm package) that currently ships broken. **0.2.0** ships code fixes (`install.js` error classification, `--validate` error classification) and CI additions (smoke job, README lint) that have zero dependency on manual harness verification. **0.3.0** ships the docs restructure, scoped npm package, and 6-harness config snippets after the Harness Verification Records (glossary term) are complete.

## Context

11 onboarding changes were committed across ADRs 0021-0026 and Q7-Q10. The manual harness verification for 6 harnesses (ADR-0022, glossary: Harness Verification Record) is the long pole — 1-2 weeks of human work. The `npx -y mssql-mcp` typo (ADR-0023) is a currently-shipping broken install command that can't wait for a versioned release. ADR-0014's dual stability contract permits CLI/error-shape changes within `0.x`.

## Decision

### Phase 1 — `0.1.1` (immediate, doc-only)

Fix `npx -y mssql-mcp` → `npx -y mssql-mcp-cli` in the README. This is a patch to the current `0.1.0` — no code changes, no binary rebuild, no CI changes. The broken install command ships fixed as fast as a README edit + push lands.

(Once ADR-0023's scoped package `@codegiveness/mssql-mcp` is live in `0.3.0`, the README will use that name. `0.1.1` is a stopgap that fixes the typo against the current `mssql-mcp-cli` package name.)

### Phase 2 — `0.2.0` (days, code + CI)

No dependency on manual harness verification. Ships:
- `install.js` download-failure classification (Q7): distinguish HTTP 404 / network error / timeout in error messages.
- `ConnectionValidator.ValidateAsync` error classification (Q8): classify by `SqlException.Number`, prefix with `[connection]` / `[auth]` / `[timeout]` / `[certificate]`.
- CI smoke job (ADR-0026): `npx ... --version` + `--validate` against Azure SQL Edge.
- CI README snippet lint (ADR-0026): parse every JSON block in README, assert valid JSON + `mcpServers` shape + `command`/`args`/`env` keys.

### Phase 3 — `0.3.0` (1-2 weeks, docs + package, after Harness Verification Records complete)

Ships:
- Scoped npm package `@codegiveness/mssql-mcp` (ADR-0023), dual-published with `mssql-mcp-cli`.
- README restructure into 15 sections (ADR-0025): hero block, platform-aware quickstart, supported clients table, verify-it-works section, troubleshooting section, comparison table (Q10).
- 6 harness config snippets (ADR-0022) — gated on the Harness Verification Records being complete for all 6.
- Troubleshooting tables (Q6): symptom/fix table + per-harness where-to-look table with "what connected looks like" column.

## Considered Options

- **A. One-shot `0.2.0`** — rejected. Bundles fast-shipping code fixes with slow-shipping manual verification, delaying the code fixes for no benefit. The `--validate` error shape change lands untested by real users.
- **B. Phased: `0.1.1` typo fix, `0.2.0` code+CI, `0.3.0` docs+package ✅** — chosen. Code fixes ship in days; docs+package ship once verification is complete; the currently-broken install command is fixed immediately.
- **C. Everything to `1.0.0`** — rejected. Delays all fixes until the ADR-0018 graduation gate (30-day dogfood + security research, weeks/months away). The broken `npx -y mssql-mcp` command ships for that entire window. Directly violates "always 100% worked."

## Consequences

- Three releases instead of one — but each is smaller, lower-risk, and faster to ship.
- `0.1.1` is a README-only patch: no binary rebuild, no npm republish, no release pipeline run. Just edit + push + the npm readthedocs/GitHub renders the fix.
- `0.2.0`'s `--validate` error shape change is a minor-version-appropriate change per ADR-0014.
- `0.3.0`'s scoped package is additive — `mssql-mcp-cli` stays published, so no existing user breaks.
- The Harness Verification Records (glossary term) are the gate for `0.3.0` — `0.3.0` cannot ship until all 6 are recorded.
- This ADR does not alter the ADR-0018 graduation path (`0.1.0` → dogfood + security research → `1.0.0-rc.1` → `1.0.0`). `0.2.0` and `0.3.0` are intermediate minors within the `0.x` series.

## Amendment (0.3.0 release)

The original Phase 3 plan shipped "docs restructure, scoped npm package, and 6-harness config snippets after the Harness Verification Records are complete." During 0.3.0 release prep, the scope was adjusted:

**0.3.0 (actual)** ships the scoped npm package + ADR-0028 binary delivery overhaul (optionalDependencies + shim self-heal), README restructure, and the NuGet version-sync fix. The install fix is the high-value change — it eliminates the `postinstall`-didn't-run failure class that broke `npx -y @codegiveness/mssql-mcp`. This could not wait for manual harness verification.

**0.3.1 (deferred)** ships the 6 harness config snippets (#37), verify-it-works + troubleshooting sections (#38), gated on the Harness Verification Records (#34) being complete. These are documentation that depends on manual human verification across 6 harnesses on real machines — a human-in-the-loop task, not a code task.

The core decision (phased shipping) is intact. The adjustment splits Phase 3 into a code release (0.3.0) and a docs release (0.3.1) so the install fix is not blocked by manual verification work.
