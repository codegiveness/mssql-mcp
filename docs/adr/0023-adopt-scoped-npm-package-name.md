# Adopt scoped npm package name `@codegiveness/mssql-mcp`

Publish a second npm package, `@codegiveness/mssql-mcp`, using the same `install.js` and binary as the existing `mssql-mcp-cli`. Document `npx -y @codegiveness/mssql-mcp` as the canonical install command. Keep `mssql-mcp-cli` published for backward compatibility; deprecate it once the scoped name is stable and adoption has migrated.

## Context

The README's headline quickstart said `npx -y mssql-mcp`, but the actual npm package is named `mssql-mcp-cli`. The name `mssql-mcp` on npm is owned by a different project (v2.3.5) — so the documented command installed the wrong package. Even after a doc fix to `mssql-mcp-cli`, the UX stays ugly: the install name (`mssql-mcp-cli`) doesn't match the invocation name (`mssql-mcp`, the binary the install places). Reference repos (codegraph `@colbymchenry/codegraph`, rtk `rtk`, agentmemory `@agentmemory/agentmemory`) all keep install name ≈ invocation name.

## Decision

- **New canonical package**: `@codegiveness/mssql-mcp` (scoped, always available, signals "official from codegiveness"). Install command: `npx -y @codegiveness/mssql-mcp`. Binary placed: `mssql-mcp`. Install name matches invocation name modulo the scope prefix.
- **Same `install.js`, same binary** — only a second `package.json` publish target. No code changes to the installer logic.
- **`mssql-mcp-cli` stays published** for backward compatibility. Existing users keep working. Mark deprecated in npm description once migration is underway.
- **README updated** to use `@codegiveness/mssql-mcp` as the documented one-liner; `mssql-mcp-cli` mentioned only in a migration note.
- **Dual-publish in CI**: the release workflow publishes both packages from the same release artifact.

## Considered Options

- **A. Doc fix only (`npx -y mssql-mcp-cli`)** — rejected. Fixes the typo but leaves install name ≠ invocation name, a confusion the reference repos all avoid. Also leaves the npm package description reading "mssql-mcp-cli" which is forgettable.
- **B. Rename to unscoped `mssql-mcp`** — rejected. Name is taken on npm by a different project (v2.3.5). Dead end unless the owner transfers it.
- **C. Scoped `@codegiveness/mssql-mcp`, dual-publish ✅** — chosen. Install name ≈ invocation name, scoped names are always available, matches the codegraph pattern (`@colbymchenry/codegraph`), and dual-publishing means zero breakage for existing `mssql-mcp-cli` users.

## Consequences

- CI release workflow gains a second `npm publish` step for `@codegiveness/mssql-mcp` (same tarball, different `package.json` `name`).
- README, all harness snippets, and any future `configure` docs use `@codegiveness/mssql-mcp`.
- `mssql-mcp-cli` remains installable; a deprecation banner is added to its npm page once migration is underway.
- The npm badge in the README points to the scoped package version.
- Two npm packages to maintain in sync until `mssql-mcp-cli` is retired — but they share one `install.js` and one binary, so the maintenance cost is a second publish step, not a second codebase.
