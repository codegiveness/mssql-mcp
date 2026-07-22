# NuGet provenance: skip attestation, adopt Trusted Publishing

For v1.0.0 supply-chain hardening, adopt NuGet Trusted Publishing (OIDC, no long-lived API key) and `actions/attest@v4` for GitHub Release artifacts + npm packages, while skipping `actions/attest@v4` for the `.nupkg` because NuGet.org re-signs packages on ingestion. **New** (supersedes nothing).

## Context

NuGet.org re-signs every package on ingestion with the NuGet repository signature, replacing the package's binary bytes — which breaks any source SHA captured by `actions/attest@v4` before upload (see [NuGetGallery#10026](https://github.com/NuGet/NuGetGallery/issues/10026)). Microsoft's first-party NuGet packages (e.g. `Microsoft.Data.SqlClient`, `Microsoft.Extensions.*`) do not carry provenance attestations; only ~5 small OSS peers ship them, each via a workaround that does not generalize to a `dotnet tool` package. The project needs supply-chain hardening for v1.0.0: no long-lived secrets in CI, SHA-pinned actions, and provenance attestations on every artifact that can carry one. The previous release workflow (ADR-0014) pushed NuGet with a long-lived `NUGET_API_KEY` secret and attested nothing.

## Decision

1. Adopt Trusted Publishing via `NuGet/login@v1.2.0` (OIDC from GitHub Actions) — eliminates the long-lived `NUGET_API_KEY` and gives NuGet.org an auditable, per-workflow trust boundary.
2. Skip `actions/attest@v4` for the `.nupkg` — the attestation would be invalidated by NuGet.org's re-signing step.
3. Attest GitHub Release archives (`.tar.gz`, `.zip`) and checksums (`.sha256`) via `actions/attest@v4.2.0`, and publish npm packages with `--provenance` (both require `id-token: write` on the release job).
4. Track native NuGet package provenance via [NuGet/Home#13581](https://github.com/NuGet/Home/issues/13581) for future adoption once the upstream feature lands.

The `actions/attest@v4` step runs AFTER `gh release create` (so the artifacts exist on the release) and BEFORE the NuGet/npm pushes (so an attestation failure aborts the release before any registry publish). NuGet's Trusted Publishing login runs immediately before `dotnet nuget push` and sets the API key via OIDC — no `--api-key` flag, no `NUGET_API_KEY` secret reference.

## Considered Options

- **A. Attest everything including the `.nupkg`** — rejected. NuGet.org re-signs on ingestion (NuGetGallery#10026), so the attestation SHA would not match the bytes users download; the attestation would be provably wrong rather than merely absent.
- **B. Use `PackageLicenseFile` instead of a license expression** — rejected. Unrelated to provenance; the license expression was already chosen in #17.
- **C. Keep the long-lived `NUGET_API_KEY`** — rejected. Long-lived secret with no OIDC audit trail, no per-workflow scoping, and no rotation story. Trusted Publishing is strictly better for a solo-maintained project.
- **D. Adopt Trusted Publishing + attest GitHub Release/npm, skip `.nupkg`** ✅ — chosen. The only option that hardens every artifact whose bytes are not mutated after upload.

## Consequences

- Trusted Publishing requires a one-time manual NuGet.org trusted-publisher configuration: link the GitHub repo + workflow file + environment on the NuGet package's "Publish" settings. This is done outside code and is a prerequisite for the first Trusted Publishing push.
- The `NuGet/login@v1.2.0` action requires a `NUGET_USERNAME` repo secret (the NuGet.org account name) and outputs a short-lived `NUGET_API_KEY` consumed by `dotnet nuget push --api-key`. The long-lived `NUGET_API_KEY` secret is deleted after the first successful Trusted Publishing push is verified (tracked in #19's release verification).
- npm provenance requires `id-token: write` on the `build-and-release` job (added to `release.yml`); the top-level workflow `permissions` stays `contents: read`.
- Consumers who verify GitHub Release or npm artifacts via `gh attestation verify` or `npm audit signatures` get a cryptographically-verifiable link back to this repo; NuGet consumers do not (the package is repository-signed by NuGet.org but not provenance-attested). The gap is documented in `SECURITY.md` and tracked via NuGet/Home#13581.
- If NuGet/Home#13581 ships native provenance, this ADR's Decision 2 reverses (add `actions/attest@v4` for the `.nupkg`); Decisions 1, 3, 4 stand.

## Status

Accepted (2026-07-21). New (supersedes nothing).
