# Build & release pipeline: tag-triggered + CI on main, dual-channel publish

Two GitHub Actions workflows. `ci.yml` runs build + test + pack (no publish) on every push to main and on PRs to catch regressions. `release.yml` runs on tag `v*.*.*` (and via `workflow_dispatch` escape hatch) to publish to GitHub Releases, NuGet, and npm sequentially. First release is `0.1.0` — signals "early, API may shift" before stability commitment.

## Workflows

### `ci.yml` (push to main, PRs)

1. `dotnet build` (all three projects)
2. `dotnet test --filter Category!=Integration` (ADR-0013)
3. `dotnet pack src/mssql-mcp/mssql-mcp.csproj -c Release` (verify nupkg builds, no push)
4. Upload build artifacts

### `release.yml` (tag `v*.*.*` or `workflow_dispatch`)

1. All CI steps
2. Build self-contained binaries per RID:
   - `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`: `dotnet publish -r <rid> --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true`
   - `win-x64`: `dotnet publish -r win-x64 --self-contained false -p:PublishSingleFile=true` (framework-dependent per ADR-0002 — SNI license blocks self-contained Windows)
3. Archive flat (binary at archive root per the Node shim contract) — `.tar.gz` for Unix, `.zip` for Windows
4. Generate SHA256 checksums
5. Create GitHub Release with archives + checksums
6. Push `mssql-mcp.<version>.nupkg` to NuGet.org
7. Sync `npm/package.json` version to tag, `npm publish`

Sequential stages prevent partial releases — if NuGet push fails, npm publish doesn't run.

## Versioning

- Semantic Versioning 2.0 for both NuGet and npm.
- NuGet `<VersionPrefix>0.3.0</VersionPrefix>` in `mssql-mcp.csproj`.
- npm `version` synced from tag (drop the `v` prefix).
- Prereleases: `v0.2.0-preview.1` tag → NuGet `-preview.1` suffix, npm `0.2.0-preview.1`.
- GitHub Release tags use the `v` prefix (`v0.1.0`); the actual version number drops it (`0.1.0`).
- Bump to `1.0.0` after first wave of public feedback signals stability.

### Dual stability contract (0.x → 1.0.0)

**Tool surface is stable from `0.1.0`** — tool names, parameter names, and parameter types don't break within the `0.x` series. New tools and optional params can be added (non-breaking). Breaking changes to tool schemas require a `1.0.0` bump or a new tool name.

**Everything else is unstable in `0.x`** — CLI flags, env var names, error response shapes, return value formats, ADRs. These can change between `0.1.0` and `0.2.0`. Documented in CHANGELOG.

### `0.1.0` → `1.0.0` graduation triggers

Bump to `1.0.0` when ALL of these are true:

1. **Production usage** — at least one person (maintainer or external user) is using mssql-mcp daily against a production SQL Server for ≥ 30 days without a data-loss incident.
2. **Guard audit** — the Guard has survived a public security review (a security researcher or contributor attempted to bypass it and either failed or found issues that were fixed).
3. **Tool surface stable** — no tool added/removed/renamed for one full minor release cycle (e.g. `0.3.0` → `0.4.0` with no tool surface changes).
4. **Distribution verified** — `npm install` and `dotnet tool install` both work on all 5 RIDs without manual intervention.
5. **Test coverage** — unit test count ≥ 80, Guard AST validation has ≥ 30 test cases covering all known attack vectors from ADR-0006.

If any one of these isn't met, stay on `0.x`.

### Release candidates

- `1.0.0-rc.1` — release candidate after all 5 triggers above are met. Promoted to `1.0.0` after 7 days with no blocking issues.
- `0.x.0-preview.N` — for features not ready for the next minor. Agents shouldn't pin to these.

### Documentation

- **CHANGELOG.md** — human-readable list of changes per version, generated from conventional commits.
- **README** — a "Stability" note: "mssql-mcp is currently `0.x`. The tool surface is stable; CLI/env/return shapes may change between minor versions before `1.0.0`."

## RID matrix

| RID | Self-contained | Archive |
|---|---|---|
| `linux-x64` | ✅ | `.tar.gz` |
| `linux-arm64` | ✅ | `.tar.gz` |
| `osx-x64` | ✅ | `.tar.gz` |
| `osx-arm64` | ✅ | `.tar.gz` |
| `win-x64` | ❌ (framework-dependent) | `.zip` |

## Considered Options

- **B. Tag-triggered + nightly CI on main + manual dispatch** ✅ — chosen
- A. Tag-triggered only — rejected: no regression catch on main between releases (abandoned projects with unanswered issues are the cautionary tale)
- C. Manual dispatch only — rejected: standard tag-triggered release is the .NET and npm pattern; manual-only adds friction without benefit

## Consequences

- `git tag v0.1.0 && git push --tags` is the release command. No other ceremony.
- `workflow_dispatch` escape hatch lets you re-run a failed release stage without retagging.
- Windows npm users get a framework-dependent binary (need .NET 10 runtime) — documented in README with `dotnet tool install` as the alternative.
- NuGet `dotnet tool` install works cross-platform with no SNI license concern (ADR-0002).
- npm package version always matches the git tag — single source of truth.
- First release is `0.1.0` — no stability commitment. API may shift before `1.0.0`.

## `install.js` contract (npm postinstall)

> **⚠ Superseded by [ADR-0028](./0028-binary-delivery-via-optional-dependencies-and-shim-self-heal.md)** — binary delivery moved to `optionalDependencies` + shim self-heal. `install.js` and the postinstall script are deleted. The contract below is retained for historical reference only.

The `npm/install.js` postinstall script depends on these release artifacts:
- One flat archive per RID (binary at archive root, per the Node shim contract) — `mssql-mcp-{version}-{rid}.tar.gz` (Unix) or `.zip` (Windows)
- One SHA256 checksum file per archive — `mssql-mcp-{version}-{rid}.tar.gz.sha256`
- Archives and checksums attached to the GitHub Release for the matching tag

`install.js` behavior contract (captured in grilling Q23, not ADR-worthy on its own — reversible):
- Fail loudly on any error (no network, 404, checksum mismatch, extraction failure, unsupported platform) with a clear message naming the RID, URL tried, and `dotnet tool install mssql-mcp` fallback
- No silent fallback to the Node shim (shim exists only so `npx` works immediately post-install, not as a real server)
- No retry logic in v1
- Detect unsupported platform/arch BEFORE attempting download; print supported RIDs
- Verify SHA256 after download; fail on mismatch
- Print final binary path on success
- Exit non-zero on any failure so `npm install` reports the failure

## Trimming journey (issue #44)

- **v0.3.0 and earlier**: `PublishTrimmed=true` on Unix RIDs. Worked because serialization used reflection-based `System.Text.Json` with `[UnconditionalSuppressMessage("Trimming", "IL2026")]` suppressing the build warning. But the suppression only hid the build warning — the runtime crash (`InvalidOperationException: Reflection-based serialization has been disabled`) was not prevented. Issue #44 reported this as "all tools crash on SQL Server 2025" (the reporter's diagnosis pointed at SQL Server types; the actual root cause was trimming disabling reflection).

- **v0.3.2**: `PublishTrimmed=false` on Unix RIDs. Quick fix to unblock Linux/macOS users. Binary size increased from ~15 MB to ~30 MB. No code changes.

- **v0.4.0**: `PublishTrimmed=true` restored. All serialization migrated to source-generated `McpJsonContext` (a `JsonSerializerContext` subclass with explicit `[JsonSerializable]` registrations). All anonymous-type error payloads replaced by explicit `record` DTOs. All `[UnconditionalSuppressMessage]` attributes removed. Binary size is ~30 MB (same as v0.3.2's untrimmed size). While trimming is re-enabled, the source-generated `McpJsonContext` registers `object` and `Dictionary<string,object?>` for polymorphic row serialization, which forces the trimmer to retain more metadata than the original reflection-based approach. The binary is functional and unblocks all Linux/macOS users. A future ticket may narrow the type set to recover the ~15 MB size.

## JSON-RPC release smoke step (v0.4.0)

The `smoke` job in `release.yml` now includes a JSON-RPC smoke step that:
1. Starts Azure SQL Edge in a container
2. Sends `initialize` + `notifications/initialized` + `tools/call` (list_databases) over stdio to the published binary
3. Fails the release if the response contains the crash envelope (`"An error occurred invoking '<tool>'."`) or lacks a JSON-RPC `result` field

This regression guard would have caught issue #44 before any user hit it. It runs on every release-tagged build, against the exact trimmed artifact users will install.
