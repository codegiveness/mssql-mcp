# Release gate and provenance

## Status

Accepted by maintainer direction (2026-11-19). Supplements ADR-0019 (NuGet Trusted Publishing, attestation of GitHub Release archives), ADR-0033 (solo-maintainer branch protection) and ADR-0037 (npm staged publishing). The 0.x release policy and npm staging are unchanged.

## Context

`release.yml` built and published from a tag push or a `workflow_dispatch` from `main` with no check that the commit had passed CI, no human step before NuGet and the npm staging step, and no published provenance file. Three gaps followed:

- A release could start from a commit whose required checks were red, still running or never ran. `release-please.yml` dispatches `release.yml` right after the release PR merges, while the push workflows for that commit are still running.
- Anyone or anything able to run the workflow on `main` could use the NuGet OIDC login and stage npm packages without a second decision. ADR-0037 puts a human at the npm approval step, but NuGet has no such step.
- The workflow attested the release archives, checksums and SBOMs, but the Sigstore bundle was never attached to the release. Consumers and OpenSSF Scorecard (Signed-Releases) could not find a provenance file next to the artifacts.

The sibling project `codegiveness/postgresql-sharp-mcp` already runs with an environment gate, per-job timeouts and a published bundle. These controls are adopted here; the MCP server implementation itself is not changed.

## Decision

1. **Environment gate.** The jobs that use the NuGet OIDC identity or stage npm packages (`build-and-release` and `postgresql-nuget-033`) run in the GitHub environment `release`. The environment requires a reviewer (the maintainer) and deploys from `main` only. The approval prompt appears after the gates below are green, so the reviewer approves a run that has already passed. This is an additional gate: ADR-0037 still holds that the check on the exact npm artifact is the 2FA approval on npm, which the environment does not replace.
2. **Preflight CI-green gate.** Job `preflight` fails unless the release commit (`github.sha`) is on `main` and the newest check run of each required check on that commit is `success` or `skipped`. `skipped` passes as it does in branch protection: `check` (version consistency) skips release-please's own `chore(main): release` commits by design. The job uses its `GITHUB_TOKEN` with `checks: read` and polls for up to 45 minutes, because release-please dispatches the release before CI finishes. The required list is the branch-protection list for checks that run on push to `main`: `build`, `Analyze (csharp)`, `Analyze (javascript-typescript)`, `sharpfuzz`, `security-regressions`, `secrets`, `workflow-audit`, `artifact-audit`, `docker`, `npm-pack`, `smoke`, `check`, `release-policy`. `validate` (semantic PR title) runs only on pull requests and was enforced on the release PR. The list is duplicated in `release.yml` because the token cannot read branch protection; changing the protected contexts means changing both. The release smoke job is named `release-smoke` so it cannot be confused with the CI `smoke` check.
3. **Verification gate.** Job `verify` runs before any artifact exists: build, `dotnet format --verify-no-changes`, unit tests and the integration tests against a SQL Server service container, using the same image digest and setup as `ci.yml`. It runs in parallel with `preflight`. `build-and-release` needs both. `ci.yml` is not reused through `workflow_call` because a called workflow inherits the caller's `github.workflow` in its concurrency group, so the two groups would collide and cancel each other, and it would repeat every CI job.
4. **Provenance bundle.** After attesting the archives, checksums and SBOMs, the workflow uploads the bundle from `steps.attest.outputs.bundle-path` as `provenance.sigstore.json` on the GitHub Release, before the NuGet push and npm staging. A failed upload stops the release before any registry sees the version. The `.nupkg` is still not attested (ADR-0019).
5. **Timeouts.** Every job in every workflow has `timeout-minutes`. `npm-pack` in `ci.yml` uses the content-locked npm CLI (`install-npm-cli`, `node "$NPM_CLI"`) instead of the runner's npm, as `release.yml` already did.
6. **Licence files travel with the binaries.** The publish output carries `LICENSE`, `THIRD-PARTY-NOTICES.md` and `LICENSES/`, so the archives and per-platform npm packages include them. The workflows copy the same files into the main npm package directory before packing or staging, and assert that archives and tarballs contain them.
7. **Agent code review.** The repository ruleset `main: Copilot code review` requests GitHub Copilot review on pull requests to `main`. It needs no workflow, no secret and no third-party action, but it runs only when the owner's Copilot plan includes code review; it did not run on the 0.6.2 pull requests. Before approving the `release` environment, the maintainer has an AI reviewer agent review the release diff and resolves its findings. For 0.6.2 that review found a preflight defect (an empty required-check name that would stall the gate), fixed before release. Neither review is a required check, and neither replaces the required checks or the environment approval.
8. **Back-fill.** The maintainer verifies the existing attestations of v0.5.5 to v0.6.1 (`gh attestation verify <artifact> --repo codegiveness/mssql-mcp`), downloads each bundle (`gh attestation download`), and uploads it as `provenance.sigstore.json` to the matching release (`gh release upload <tag> provenance.sigstore.json`). This gives the last releases the same asset the new workflow publishes. It is a one-time manual step, not part of the workflow.
9. **Dependabot.** `github-actions` updates are grouped into one pull request, and a `dotnet-sdk` ecosystem keeps `global.json` current within the .NET 11 line. The 7-day cooldown applies to both.

## Considered options

- **Required reviewer only (no preflight).** Rejected: the reviewer would approve without evidence, and a reviewer-less path (tag push) would stay ungated.
- **Re-run all of `ci.yml` through `workflow_call`.** Rejected: concurrency collision and duplicated cost (see Decision 3). Checking the push run's results plus a lean verification job gives the same assurance.
- **Fail immediately when checks are pending.** Rejected: release-please dispatches the release while CI is running, so every release would fail on its first attempt.
- **Third-party AI review action.** Rejected: adds a secret and a new supply-chain dependency. The Copilot ruleset gives the same review signal with no workflow surface.
- **Attest the `.nupkg`.** Still rejected (ADR-0019): NuGet.org re-signs packages on ingestion.

## Consequences

- A release cannot publish from a commit that is off `main` or has a failed, missing or pending required check, nor before unit and live-SQL integration tests pass, nor without a reviewer approval on the `release` environment.
- Tag-push releases run from `refs/tags/...`, which the `release` environment's `main`-only deployment policy rejects. Releases therefore go through release-please's dispatch from `main`. To allow tag pushes, add a `v*.*.*` tag rule to the environment.
- Re-running a failed release re-runs the gates. A superseded (re-run) check counts by its newest result.
- Every release now carries `provenance.sigstore.json`. Verification: `gh attestation verify <artifact> --repo codegiveness/mssql-mcp --bundle provenance.sigstore.json`.
- The required-check list in `release.yml` can drift from branch protection. Review it when a check is added to or removed from the protected set.
