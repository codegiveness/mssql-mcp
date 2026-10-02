# Continue 0.x releases and distinguish harness evidence from examples

## Status

Accepted by maintainer direction. Supersedes ADR-0014's major-version graduation and release-candidate requirements, ADR-0018's graduation path and calendar, ADR-0022's mandatory six-harness manual-verification gate, and ADR-0027's remaining documentation release gate. Supplements ADR-0034 with an enforced 0.x release policy. Historical decisions and audit records remain intact; they are not evidence that their original gates passed.

## Context

The latest release at this decision is `0.5.5`; no `1.0.0-rc` release has been made. The maintainer directs continued `0.x` releases, not a timed promotion to `1.0.0`. The user reports successful production use with **Oh My Pi** and **OpenCode**. That report is useful adoption evidence, but does not provide a dated 30-day daily-use log, client versions, OS/configuration details, or manual verification records for six other harnesses.

The old launch milestones tied delivery to a 30-day dogfood calendar, a v1 release candidate and seven-day promotion clock, and six manually verified harness configurations. Treating those gates as completed would invent evidence. Keeping them as mandatory release blockers would contradict the maintainer's revised direction.

## Decision

1. **Continue 0.x releases.** No automatic or manual release path may publish artifacts for a major version greater than or equal to one. There is no scheduled v1 graduation or RC promotion. Changing this policy requires a new explicit maintainer decision and corresponding guard changes, not just a tag or a `Release-As` override.
2. **Retire old launch milestones, not certify them.** Issues #1, #11, #20, #22, and #34 are retired as launch milestones under this policy. Retirement does not mean their original acceptance tests, 30-day logs, five-RID installations, or six GUI harness checks were completed. Bug fixes, security findings, and distribution regressions remain actionable independently of the retired milestones.
3. **Separate the evidence classes.** Describe Oh My Pi and OpenCode success as user-reported production use. Describe the automated MCP stdio proof by what it exercises: initialize, tool discovery, and a real tool call against SQL Server. It does not verify GUI integration, every harness version, or every operating system. Preserve useful configuration and troubleshooting examples, clearly labeled as examples rather than manually verified harness records. Unknown client versions, dates, paths, and logs remain unknown.
4. **Enforce the policy before publication.** Retain `bump-minor-pre-major: true` in release-please configuration. Use one reusable `scripts/check-release-policy.js` validator for the canonical manifest, generated release tags, pushed tags, and `workflow_dispatch` input. Admit v-prefixed SemVer tags only when major is zero, including valid 0.x prereleases; reject malformed tags, missing manual input, and all majors >= 1 (including `v1.0.0-rc.1`). Check the manifest before release-please can create a release, and check both the manifest and selected tag before the release workflow syncs stamps, builds, or publishes any artifacts. The PR/push policy check includes bot-generated release PRs even where stamp consistency is skipped.
5. **Preserve existing controls.** The Guard, least-privilege guidance, security reviews and vulnerability fixes, distribution architecture and checksums, Trusted Publishing/provenance, SBOM/attestation, version consistency, CI checks, and pre-push requirements are unchanged. The tool surface remains stable in 0.x: use additive tools or optional parameters rather than breaking existing tool schemas. CLI/configuration/error/return-shape changes remain documented between minor releases under the existing stability contract.

## Considered options

- **Mark historical gates complete from current production use:** rejected. A user report is not a substitute for missing dated logs or six per-harness verification records.
- **Keep the old v1 calendar and six-harness gate:** rejected. It would retain obsolete release requirements despite explicit maintainer direction.
- **Continue 0.x with honest evidence labels and a shared release guard:** chosen. Allows verified fixes and documentation to ship without weakening runtime, security, or distribution controls.

## Consequences

- Old launch issues can be closed as superseded/retired, with a link to this decision; do not describe them as passing their original acceptance criteria.
- Current documentation can include unverified harness examples. Manual verification limits are explicit, and a harness-specific bug is still a valid bug report.
- A forced major release PR fails policy CI and the release-please manifest gate; a major tag or manual dispatch fails before artifact production/publication.
- Policy tests exercise accepted 0.x tags, rejected majors and malformed input, tag-push selection, manual dispatch selection/output behavior, and the manifest CLI. Tests do not create tags, releases, or registry publications.
- This decision changes no version stamp and does not itself create a release.
