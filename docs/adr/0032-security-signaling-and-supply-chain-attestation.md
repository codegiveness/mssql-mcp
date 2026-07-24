# Security signaling and supply-chain attestation

We add OpenSSF Scorecard (public action + live badge), CycloneDX SBOM generation in CI with attestation on GitHub Release artifacts, and OpenSSF Best Practices / SBOM / Security Policy badges in the README. This makes the existing security posture (Guard, Trusted Publishing, SHA-pinned actions, provenance attestation) *visible and verifiable* by adopters doing due diligence, and closes the gap between "we are secure" and "we can prove it."

## Context

The project already has a strong runtime security posture: 4-layer Guard validation, read-only transactions, password obfuscation on all log sinks, structured ADR-0010 error shapes, SHA-pinned GitHub Actions, Trusted Publishing via OIDC, npm `--provenance` attestation, Dependabot, CODEOWNERS, and a pre-public security audit (`docs/security-audits/2026-07-22-pre-public.md`) that fixed 5 blocking findings. None of this is *signaled* to adopters — the README has 5 badges (CI, NuGet, npm, .NET, License), none of which communicate security posture. An enterprise adopter evaluating "is this safe to point at my production SQL Server?" has to read the source to find out.

OpenSSF Scorecard (`ossf/scorecard-action`) runs automated heuristics (branch protection, dependency pinning, CI best practices, etc.) on every push to `main` and publishes a 0–10 score to the repo's Security tab with a live badge (`api.securityscorecards.dev`). This is the canonical OSS security signaling mechanism — used by CNCF, OpenSSF, and major projects.

An SBOM (Software Bill of Materials) is a machine-readable dependency inventory. Scorecard checks for its presence as an artifact (not just the GitHub dependency graph). CycloneDX is the natural choice for .NET: the `cyclonedx-dotnet` tool generates it from the `.sln` with one command. SPDX is the alternative, but `cyclonedx-dotnet`'s primary output is CycloneDX, and CycloneDX is the format security-vulnerability tooling (Dependency-Track, OWASP tools) consumes natively.

Snyk was considered for continuous vulnerability scanning + badge, but rejected because Dependabot already covers NuGet vulnerability alerts — adding Snyk duplicates that with a separate GitHub App, separate config, and a vendor-branded badge. Not worth the maintenance for a solo-maintained project.

## Decision

1. **OpenSSF Scorecard via public GitHub Action.** Add `ossf/scorecard-action` to a new `.github/workflows/scorecard.yml` triggered on push to `main` + weekly cron. Results publish to the Security tab. Add the live badge to the README badge row: `[![OpenSSF Scorecard](https://api.securityscorecards.dev/github.com/codegiveness/mssql-mcp/badge.svg)](https://securityscorecards.dev/viewer/?raw=github.com/codegiveness/mssql-mcp)`.

2. **CycloneDX SBOM in CI, attested on Release.** Add `dotnet tool install -g CycloneDX && cyclonedx mssql-mcp.sln` as a step in `ci.yml` producing a `.bom.json` artifact. In `release.yml`, generate the SBOM and attest it via `actions/attest@v4` alongside the existing Release archive attestation. The SBOM is attached to the GitHub Release as an asset.

3. **Three new README badges:**
   - OpenSSF Best Practices (`bestpractices.dev`) — self-assessed checklist, links to our profile
   - SBOM (static shields.io badge linking to latest Release `.bom.json`)
   - Security Policy (static shields.io badge linking to `SECURITY.md`)

4. **Skip Snyk.** Dependabot covers NuGet vulnerability alerts. Snyk would add a duplicate scanning app + vendor badge with no marginal value.

5. **Skip code coverage badge.** Orthogonal to security. The ~300 test count is already in the README. Coverage instrumentation adds CI complexity (coverage tooling, upload step, token management) without directly answering the security question. Revisit at 1.0.

6. **`docs/security-posture.md` consolidation page.** A single page linking to all security evidence: Scorecard badge, Best Practices badge, SBOM artifact, security audit reports, SECURITY.md, branch protection settings, Trusted Publishing + provenance attestation. This is the URL to share in marketplace listings, security reviews, and enterprise procurement questionnaires. Distinct from the README's Security section (which describes runtime Guard mechanics) — the posture doc collects *supply-chain and process* evidence.

## Considered Options

- **A. CycloneDX in CI + attested on Release ✅** — chosen. Free, standard, closes Scorecard's SBOM check, produces an exportable artifact.

- **B. SPDX via GitHub native dependency graph only — rejected.** No artifact is produced — only the GitHub UI dependency graph. Scorecard's SBOM check specifically wants an artifact. Zero work, but zero credit.

- **C. Skip SBOM for now — rejected.** Defensible for 0.x, but `cyclonedx-dotnet` is a one-liner and the Scorecard check is automatic. Missed opportunity.

- **D. Snyk for vulnerability scanning + badge — rejected.** Dependabot already covers NuGet alerts. Snyk duplicates that with a separate app, separate config, and a vendor-branded badge. Not worth the maintenance for a solo project.

- **E. Code coverage badge — rejected.** Not a security signal. Adds CI complexity without directly answering the security question. Revisit at 1.0.

## Consequences

- **Scorecard runs on every push to `main` + weekly.** Results are public in the Security tab. The badge updates live. Low scores surface specific gaps (e.g., "Binary-Artifacts check failed because...") that feed into the "close gaps" phase.

- **SBOM is generated on every CI build and attested on every Release.** Adopters can download `.bom.json` from the Release page and feed it into their dependency-vulnerability tooling (Dependency-Track, Grype, etc.). The attestation proves the SBOM was produced by this repo's CI, not fabricated.

- **README badge row grows from 5 to 9 badges.** This is a lot, but each is load-bearing for an adopter doing due diligence on a tool that touches their database. If trimming is needed later, `.NET` and `License` are the most expendable (already implied by context).

- **`docs/security-posture.md` becomes the canonical security evidence URL.** One link for marketplace listings, security reviews, and procurement. Must be kept in sync when new audits land or posture changes — add to the pre-push checklist (ADR-0031).

- **OpenSSF Best Practices self-assessment is a one-time ~30 min exercise.** Produces a permanent badge URL at `bestpractices.dev`. Most answers are "yes, see SECURITY.md / CI / CODEOWNERS / etc." Re-assess when posture changes.
