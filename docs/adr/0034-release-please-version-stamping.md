# Release-please for automated version stamping and CHANGELOG generation

We adopted [release-please](https://github.com/googleapis/release-please) with a custom manifest (`.release-please-manifest.json`) as the single source of truth for the version, driving automatic bumps of all version stamps (`mssql-mcp.csproj`, `npm/package.json` including five `optionalDependencies`, `server.json`'s three version fields), `CHANGELOG.md` generation from Conventional Commits, and tag creation. The existing tag-triggered `release.yml` builds and publishes unchanged. A `version-consistency.yml` CI workflow plus local `scripts/check-version-consistency.js` script enforce that no stamp ever drifts from the manifest.

## Context

Before this ADR, the repo carried the version string `0.4.2` in five places, updated by hand: `mssql-mcp.csproj` (`<VersionPrefix>`), `npm/package.json` (`version` + five `optionalDependencies`), `server.json` (three `version` fields — top-level, npm package, NuGet package), `README.md` (prose on line 60), and `CHANGELOG.md` (manual entries). The release workflow (`release.yml`) already synced the `.csproj` and `npm/package.json` versions to the git tag at build time via `sed` and `node -e`, but those were throwaway in-workspace edits — they were never committed back to `main`. This left the repo files perpetually at the last manually-bumped version, creating two failure modes: (1) `README.md` and `server.json` could silently drift since nothing checked them, and (2) the in-workspace version sync meant the committed `0.4.2` in `.csproj`/`package.json` was always one release behind the actual published version after the first post-0.4.2 release.

The repo already enforced Conventional Commits PR titles via `semantic.yml` (the `amannn/action-semantic-pull-request` action, types: `feat fix docs style refactor perf test chore ci build revert`). This is the exact commit-message fuel release-please consumes. The question was whether to keep manual version bumps with a CI validator, adopt release-please for full automation, or use a tag-driven sync-and-commit approach.

`README.md:60` contained the literal string `mssql-mcp 0.4.2` in onboarding prose ("You should see `mssql-mcp 0.4.2`"). This is a version advertisement, not a functional requirement — `mssql-mcp --version` exists for exactly this purpose, and the npm/NuGet badges at the top of the README already display the current version dynamically.

## Decision

1. **release-please with a custom manifest as the single source of truth.** A `.release-please-manifest.json` at the repo root holds the canonical version (starting at `0.4.2`). release-please's `simple` strategy bumps it. All other version carriers are derivatives.

2. **`extra-files` stamps the standard formats.** release-please natively bumps `mssql-mcp.csproj`'s `<VersionPrefix>` and `npm/package.json`'s `version` field (the five `optionalDependencies` entries are version-pinned to the package version and bumped in lockstep).

3. **`scripts/sync-server-json.js` stamps `server.json`.** The MCP `server.json` schema has three `version` fields (top-level, npm package, NuGet package) in a non-standard layout. A 20-line Node script reads the manifest version and writes all three. The same script is invoked by the consistency check, so there is one code path for writing and verifying `server.json`.

4. **`CHANGELOG.md` is auto-generated.** release-please assembles it from Conventional Commit titles since the last release. No manual changelog entries.

5. **`README.md` literal version removed.** Line 60's `mssql-mcp 0.4.2` is replaced with `mssql-mcp --version`. One fewer stamp to maintain; the README never drifts.

6. **release-please creates the tag; `release.yml` is untouched.** release-please opens a Release PR. On merge, it creates the `vX.Y.Z` tag using the built-in `GITHUB_TOKEN`. The existing `release.yml` trigger (`on: push: tags: ['v*.*.*']`) fires unchanged — it builds five RIDs, publishes to NuGet (Trusted Publishing) and npm (provenance), creates the GitHub Release with `--generate-notes`, attests artifacts, and runs the smoke job. Clean separation: release-please owns version + tag; `release.yml` owns build + publish.

7. **`version-consistency.yml` + `scripts/check-version-consistency.js` enforce integrity.** A new CI workflow runs on every PR and push to `main`. It calls the same script developers run locally. The script reads the manifest version and asserts every stamp matches. Added to the pre-push checklist in `AGENTS.md`, mirroring the existing `scripts/lint-readme-snippets.js` pattern.

8. **Bootstrap with `bootstrap-sha: 2458379`.** This is the commit of "chore(release): bump version to 0.4.2" — the last manual release. release-please scans only commits after this SHA, so the first Release PR targets `v0.5.0` (triggered by this setup commit, which is itself a `feat:`).

9. **`GITHUB_TOKEN`, no PAT.** release-please creates the tag with the built-in `GITHUB_TOKEN`. This works because `release.yml` is triggered by tag pushes, not branch pushes — the well-known limitation that `GITHUB_TOKEN`-created events don't trigger downstream workflows does not apply. No additional secrets to manage.

## Considered Options

- **A. release-please with custom manifest (Pattern 2) ✅** — chosen. Neutral across NuGet/npm ecosystems; the manifest is the source of truth and every consumer is an `extra-files` target or script-sync target. Adding a future sixth stamp is one config line. CHANGELOG is auto-generated. Proven by the existing Conventional Commits enforcement.

- **B. Pre-push validator only (architecture A)** — rejected. Catches drift but doesn't eliminate the manual toil of bumping five files by hand. The user explicitly asked for automatic updates; a validator alone is a half-measure.

- **C. Tag-driven sync-and-commit (architecture C)** — rejected. You push a tag manually, CI bumps all files, commits back to `main`, then builds. Simpler than release-please but loses auto-CHANGELOG, requires manual version-number decisions, and the commit-back-to-main-during-release step is fragile (race conditions with other PRs).

- **D. npm-centric manifest (Pattern 1)** — rejected. Would privilege `npm/package.json` as primary, but NuGet's `<VersionPrefix>` is a *floor* (ADR-0014), not the source of truth — the tag is. A neutral manifest avoids ecosystem bias.

- **E. csproj-centric manifest (Pattern 3)** — rejected. Would require a custom extractor for `<VersionPrefix>`. Adds complexity for no gain over the neutral manifest.

- **F. release-please creates GitHub Release (trigger R3)** — rejected. Conflicts with `release.yml`'s existing `gh release create --generate-notes` call. Would require removing the release-creation step from `release.yml` and trusting release-please's notes — a bigger change for no functional gain.

- **G. Keep literal version in README.md, rely on consistency check (option P3)** — rejected. Keeps a stamp that exists only for version advertising. `--version` and the dynamic badges already serve that purpose. Removing it eliminates a whole class of drift.

- **H. Use a PAT for release-please (option T2)** — rejected. No functional gain: `release.yml` fires on tag push, not branch push, so the `GITHUB_TOKEN` limitation doesn't apply. A PAT adds secret management overhead.

## Consequences

- **Repo files never drift.** All five version stamps (now four, after removing the README literal) are written by release-please or its sync script in a single Release PR commit. The consistency check is a safety net, not the primary mechanism.

- **The committed `.csproj` and `npm/package.json` now reflect the actual released version.** Previously they lagged by one release because `release.yml`'s in-workspace sync was never committed back. release-please commits the bump before the tag is created, so the repo is always at the version it ships.

- **`release.yml`'s in-workspace version sync steps (steps "Sync NuGet version" and "Sync npm version") become redundant but are kept as defense-in-depth.** They re-assert the version from the tag, which matches what release-please already committed. If release-please ever fails to commit a stamp, the build still publishes the correct version from the tag. Removing those steps is a follow-up, not part of this ADR.

- **Releases are now triggered by merging a Release PR.** The workflow is: merge feature/fix PRs to `main` → release-please opens a Release PR → merge the Release PR → tag created → `release.yml` builds and publishes. The maintainer controls timing by choosing when to merge the Release PR.

- **First release after setup is `v0.5.0`.** This setup commit is a `feat:`, so release-please will open a Release PR for `v0.5.0` on merge. This proves the pipeline end-to-end on the first cycle.

- **`CHANGELOG.md` is rewritten by release-please on the first Release PR.** The existing manual entries are preserved (release-please appends, it doesn't truncate), but future entries are auto-generated. The maintainer can edit the auto-generated entry before merging the Release PR if a human-readable summary is needed.

- **`server.json` stamping depends on `scripts/sync-server-json.js`.** If the MCP schema adds a fourth `version` field in the future, the script must be updated. This is explicit and auditable — preferable to release-please's opaque `extra-files` JSON-path matching for a non-standard layout.

- **Bootstrap SHA is a one-time config.** After the first release-please release, the `bootstrap-sha` field is no longer consulted (release-please tracks the last release tag internally). It remains in the config as a historical artifact; removing it is safe after `v0.5.0` ships.
