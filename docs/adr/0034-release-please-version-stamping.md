# Release-please for automated version stamping and CHANGELOG generation

> **Current release policy supplement:** [ADR-0036](0036-continue-zero-major-releases.md) requires continued 0.x releases. `bump-minor-pre-major: true` remains configured; one shared release-policy guard validates the manifest before release-please, generated tags before dispatch, and the manifest plus pushed/manual tag before artifact production/publication. All major versions >= 1, including v1 RCs, are blocked. The original automation decision below remains historical context; this supplement changes no version stamp.

> **Platform stamp scope:** authoritative synchronization and consistency validation include all five `npm/platforms/<rid>/package.json` versions, not only the main package's `optionalDependencies`. Release publication does not rewrite those platform versions independently; the C# repository tool's `sync-all-stamps` command owns them before build/package staging. This closes the stale-platform-version path that could cause local npm installation to select a registry version instead of the staged binary.

We adopted [release-please](https://github.com/googleapis/release-please) with a custom manifest (`.release-please-manifest.json`) as the single source of truth for the version, driving automatic bumps, `CHANGELOG.md` generation from Conventional Commits, and tag creation. The tag-triggered `release.yml` owns builds and publication. `version-consistency.yml` and the local C# repository tool's `check-version-consistency` command enforce that derivative stamps do not drift from the manifest. [ADR-0012](0012-project-structure.md#repository-tooling-cutover) records the tooling language cutover without changing version ownership.

## Context

Before this ADR, the repo carried the version string `0.4.2` in five places, updated by hand: `mssql-mcp.csproj` (`<VersionPrefix>`), `npm/package.json` (`version` + five `optionalDependencies`), `server.json` (three `version` fields — top-level, npm package, NuGet package), `README.md` (prose on line 60), and `CHANGELOG.md` (manual entries). The release workflow (`release.yml`) already synced the `.csproj` and `npm/package.json` versions to the git tag at build time via `sed` and `node -e`, but those were throwaway in-workspace edits — they were never committed back to `main`. This left the repo files perpetually at the last manually-bumped version, creating two failure modes: (1) `README.md` and `server.json` could silently drift since nothing checked them, and (2) the in-workspace version sync meant the committed `0.4.2` in `.csproj`/`package.json` was always one release behind the actual published version after the first post-0.4.2 release.

The repo already enforced Conventional Commits PR titles via `semantic.yml` (the `amannn/action-semantic-pull-request` action, types: `feat fix docs style refactor perf test chore ci build revert`). This is the exact commit-message fuel release-please consumes. The question was whether to keep manual version bumps with a CI validator, adopt release-please for full automation, or use a tag-driven sync-and-commit approach.

`README.md:60` contained the literal string `mssql-mcp 0.4.2` in onboarding prose ("You should see `mssql-mcp 0.4.2`"). This is a version advertisement, not a functional requirement — `mssql-mcp --version` exists for exactly this purpose, and the npm/NuGet badges at the top of the README already display the current version dynamically.

## Decision

1. **release-please with a custom manifest as the single source of truth.** A `.release-please-manifest.json` at the repo root holds the canonical version (starting at `0.4.2`) in release-please's path-keyed format (`{".": "0.4.2"}`). release-please's `simple` strategy bumps it. All other version carriers are derivatives.

2. **release-please manages only the manifest + CHANGELOG.** release-please cannot natively update XML (`<VersionPrefix>`) or nested JSON (`optionalDependencies`), so `extra-files` is not used. The manifest and `CHANGELOG.md` are the only files release-please touches in its Release PR.

3. **The C# repository tool's `sync-all-stamps` command syncs all stamps before release builds.** Run `dotnet run --project tools/MssqlMcp.RepoTool -- sync-all-stamps` to read the manifest and write csproj `<VersionPrefix>`, main npm `version` and optional dependencies, all five platform versions, and server.json's three version fields. One idempotent implementation supersedes the original ad-hoc shell commands and separate server-only synchronizer.

4. **`CHANGELOG.md` is auto-generated.** release-please assembles it from Conventional Commit titles since the last release. No manual changelog entries.

5. **`README.md` literal version removed.** Line 60's `mssql-mcp 0.4.2` is replaced with `mssql-mcp --version`. One fewer stamp to maintain; the README never drifts.

6. **release-please creates the tag; `release.yml` is untouched.** release-please opens a Release PR. On merge, it creates the `vX.Y.Z` tag using the built-in `GITHUB_TOKEN`. The existing `release.yml` trigger (`on: push: tags: ['v*.*.*']`) fires unchanged — it builds five RIDs, publishes to NuGet (Trusted Publishing) and npm (provenance), creates the GitHub Release with `--generate-notes`, attests artifacts, and runs the smoke job. Clean separation: release-please owns version + tag; `release.yml` owns build + publish.

7. **`version-consistency.yml` and the C# repository tool enforce integrity.** CI and developers run `dotnet run --project tools/MssqlMcp.RepoTool -- check-version-consistency`. The command asserts every stamp matches the manifest. Behavioral regressions live in `tests/MssqlMcp.RepoTool.Tests`, and the same command is required by the pre-push checklist.

8. **Bootstrap with `bootstrap-sha: 2458379`.** This is the commit of "chore(release): bump version to 0.4.2" — the last manual release. release-please scans only commits after this SHA, so the first Release PR targets `v0.5.0` (triggered by this setup commit, which is itself a `feat:`).

9. **`GITHUB_TOKEN`, no PAT.** release-please creates the tag with the built-in `GITHUB_TOKEN`. The well-known limitation that `GITHUB_TOKEN`-created events don't trigger downstream workflows applies to all event types, including tag pushes (see issue #105). To work around this, the "Trigger release.yml on new tag" step in `.github/workflows/release-please.yml:35-42` runs `gh workflow run release.yml -f tag="$tag"` via `workflow_dispatch`, explicitly dispatching the build/publish workflow with the tag name. This preserves the tag-push trigger for manual tags while avoiding PAT secret-management overhead. No additional secrets to manage.

## Considered Options

- **A. release-please with custom manifest (Pattern 2) ✅** — chosen. Neutral across NuGet/npm ecosystems; the manifest is the source of truth and every consumer is an `extra-files` target or script-sync target. Adding a future sixth stamp is one config line. CHANGELOG is auto-generated. Proven by the existing Conventional Commits enforcement.

- **B. Pre-push validator only (architecture A)** — rejected. Catches drift but doesn't eliminate the manual toil of bumping five files by hand. The user explicitly asked for automatic updates; a validator alone is a half-measure.

- **C. Tag-driven sync-and-commit (architecture C)** — rejected. You push a tag manually, CI bumps all files, commits back to `main`, then builds. Simpler than release-please but loses auto-CHANGELOG, requires manual version-number decisions, and the commit-back-to-main-during-release step is fragile (race conditions with other PRs).

- **D. npm-centric manifest (Pattern 1)** — rejected. Would privilege `npm/package.json` as primary, but NuGet's `<VersionPrefix>` is a *floor* (ADR-0014), not the source of truth — the tag is. A neutral manifest avoids ecosystem bias.

- **E. csproj-centric manifest (Pattern 3)** — rejected. Would require a custom extractor for `<VersionPrefix>`. Adds complexity for no gain over the neutral manifest.

- **F. release-please creates GitHub Release (trigger R3)** — rejected. Conflicts with `release.yml`'s existing `gh release create --generate-notes` call. Would require removing the release-creation step from `release.yml` and trusting release-please's notes — a bigger change for no functional gain.

- **G. Keep literal version in README.md, rely on consistency check (option P3)** — rejected. Keeps a stamp that exists only for version advertising. `--version` and the dynamic badges already serve that purpose. Removing it eliminates a whole class of drift.

- **H. Use a PAT for release-please (option T2)** — rejected. The dispatch workaround (`gh workflow run release.yml -f tag="$tag"` in `release-please.yml:35-42`) avoids PAT secret-management overhead while preserving the tag-push trigger for manual tags. A PAT would add secret management overhead with no functional gain over the `workflow_dispatch` dispatch.

## Consequences

- **Repo files never drift.** All five version stamps (now four, after removing the README literal) are written by release-please or its sync script in a single Release PR commit. The consistency check is a safety net, not the primary mechanism.

- **The committed `.csproj` and `npm/package.json` now reflect the actual released version.** Previously they lagged by one release because `release.yml`'s in-workspace sync was never committed back. release-please commits the bump before the tag is created, so the repo is always at the version it ships.

- **`release.yml`'s in-workspace version sync steps (steps "Sync NuGet version" and "Sync npm version") become redundant but are kept as defense-in-depth.** They re-assert the version from the tag, which matches what release-please already committed. If release-please ever fails to commit a stamp, the build still publishes the correct version from the tag. Removing those steps is a follow-up, not part of this ADR.

- **Releases are now triggered by merging a Release PR.** The workflow is: merge feature/fix PRs to `main` → release-please opens a Release PR → merge the Release PR → tag created → `release.yml` builds and publishes. The maintainer controls timing by choosing when to merge the Release PR.

- **First release after setup is `v0.5.0`.** This setup commit is a `feat:`, so release-please will open a Release PR for `v0.5.0` on merge. This proves the pipeline end-to-end on the first cycle.

- **`CHANGELOG.md` is rewritten by release-please on the first Release PR.** The existing manual entries are preserved (release-please appends, it doesn't truncate), but future entries are auto-generated. The maintainer can edit the auto-generated entry before merging the Release PR if a human-readable summary is needed.

- **`server.json` stamping is owned by `sync-all-stamps`.** If the MCP schema adds another version carrier, update this C# implementation and its consistency regressions together. There is no independent server-only synchronizer or compatibility alias.

- **Bootstrap SHA is a one-time config.** After the first release-please release, the `bootstrap-sha` field is no longer consulted (release-please tracks the last release tag internally). It remains in the config as a historical artifact; removing it is safe after `v0.5.0` ships.
