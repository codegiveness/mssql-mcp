# npm staged publishing with maintainer approval

## Status

Accepted by maintainer direction (2026-10-22). Supplements ADR-0028 §7 and ADR-0019. NuGet Trusted Publishing and GitHub Release creation are unchanged.

## Context

The release workflow published all six npm packages (`@codegiveness/mssql-mcp` and five `@codegiveness/mssql-mcp-<rid>` packages) directly from GitHub Actions with Trusted Publishing (OIDC). No human checked a version before it became installable. Anyone or anything that could run `release.yml` on `main` could therefore put code in front of every `npx @codegiveness/mssql-mcp` user.

Attacks in 2026 targeted this path directly:

- **GhostAction** (September 2025, August–October 2026) used stolen maintainer PATs to push workflows that exfiltrate CI secrets, including npm, PyPI and Docker Hub tokens. About 1,900 accounts and over 30,000 repositories were targeted in October 2026.
- **Mini Shai-Hulud** (May 2026, back in September 2026) put credential-stealing payloads into npm packages and into the `actions-cool` GitHub Actions. Mutable tags became active again when the repositories were re-enabled.

npm staged publishing (npm CLI 11.15.0+, available in the pinned npm 12.2.0) adds a proof-of-presence step. `npm stage publish` uploads a version in a non-installable state. A maintainer must approve it with 2FA through `npm stage approve` or npmjs.com. OIDC tokens cannot approve, so a compromised workflow, runner, build dependency or stolen GitHub credential can at most stage a version for review.

## Decision

1. `release.yml` runs `npm stage publish --ignore-scripts --provenance --access public` for the five platform packages and then the main package. The order, fail-fast behavior, provenance, disabled lifecycle scripts and 0.x guard stay as they were.
2. Each package's npm trusted publisher (owner `codegiveness`, repository `mssql-mcp`, workflow `release.yml`) allows `npm stage publish` only. Direct `npm publish` and `npm dist-tag` stay disabled. The workflow keeps no npm token.
3. The maintainer reviews each staged package (`npm stage list`, `npm stage view`, `npm stage download`) and approves the five platform packages before the main package. This keeps a main package from going live while its exact-version optional dependencies are missing. If a staged version is wrong, the maintainer rejects it (`npm stage reject`). The release workflow is not re-run for the same version until the staged version is rejected.
4. The release job summary lists the packages to approve. The workflow does not try to list or approve staged versions: those commands need interactive authentication.

## Considered options

- **Keep direct OIDC publishing:** rejected. It authenticates the workflow but has no human check, so a compromised workflow or dependency publishes right away.
- **Stage-only granular access token:** rejected. It brings back a long-lived secret that GhostAction-style workflows can steal. Stage-only tokens can still move dist-tags and deprecate versions.
- **GitHub environment with required reviewers before publishing:** not chosen as the gate. It approves a workflow run, not the exact tarball that npm will serve, and is controlled by the same GitHub account that the attacks target.
- **OIDC staging plus 2FA approval on npm:** chosen. The check happens on npm, with npm 2FA, on the exact artifact.

## Consequences

- A release is complete on npm only after the maintainer approves all six packages. Until then, `npm install @codegiveness/mssql-mcp` resolves to the previous version. The GitHub Release archives and NuGet package for the new version are already public.
- Before the first staged release, the maintainer turns off direct `npm publish` on each of the six trusted-publisher configurations on npmjs.com (Settings → Trusted publishing → Allowed actions). Configurations created after 2026-09-03 allow staging by default.
- `--provenance` is unchanged. Consumers verify approved versions with `npm audit signatures`.
- A failed or abandoned approval leaves staged versions that reserve the version number. Reject them before staging that version again.
