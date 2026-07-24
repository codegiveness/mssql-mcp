# Branch protection posture for solo-maintained project

We enforce branch protection on `main` with admin enforcement, CODEOWNERS enforcement, and stale review dismissal, but deliberately set `required_approving_review_count = 0` because the project is solo-maintained — requiring a second reviewer would either block all merges or devolve into rubber-stamping a second account. Signed commit enforcement is deferred because Dependabot cannot sign commits, making strict enforcement break the automated dependency-update workflow.

## Context

Branch protection on `main` was partially configured: required status checks (`build`, `validate`, strict), linear history enforced, force pushes blocked, branch deletion blocked. But `required_approving_review_count = 0`, `dismiss_stale_reviews = false`, `require_code_owner_reviews = false`, and `enforce_admins = false`. CODEOWNERS existed but was advisory — an admin could push directly to `main` bypassing everything.

OpenSSF Scorecard's `Branch-Protection` check is the highest-impact single finding for most repos. It checks: is direct push blocked? Are PR reviews required? Is stale review dismissal on? Is force push blocked? Are admins enforced? The current configuration would score poorly on the "reviews required" and "enforce admins" heuristics.

The tension: the project is solo-maintained. Requiring 1 approving review means the maintainer can never merge their own PRs without a second person. The two common workarounds are (1) require 1 review + second GitHub account rubber-stamping (theater, not security), or (2) keep 0 required reviews but enforce the other protections so direct-push and admin-bypass are blocked.

Signed commit enforcement was considered. Scorecard's `Verified-Commits` check rewards it. But enabling strict signature enforcement on `main` breaks Dependabot: Dependabot commits are unsigned, so squash-merging a Dependabot PR would fail the signature check. The maintainer would have to manually re-commit Dependabot changes with a signed commit for every dependency update — high friction for low marginal security value on a solo project.

## Decision

1. **Enforce for admins: ON.** Admins cannot bypass branch protection. This closes the "admin can push directly to `main`" gap. The maintainer must use PRs like everyone else.

2. **Require code owner reviews: ON.** CODEOWNERS becomes enforced, not advisory. For external contributors, their PRs require a maintainer review. For the solo maintainer's own PRs, code owner review is satisfied if the maintainer is in the `@codegiveness/mssql-mcp-maintainers` team.

3. **Dismiss stale reviews: ON.** When a new commit is pushed to a PR, existing approvals are dismissed. Prevents "approved, then changed, still approved" scenarios.

4. **Required approving reviews: 0 (deliberate).** The project is solo-maintained. Requiring 1 review would block all merges or require a rubber-stamp second account. This is an honest tradeoff: we accept the Scorecard point loss on this heuristic in exchange for a functional solo workflow. When a second maintainer joins, flip to 1 required review.

5. **Signed commit enforcement: OFF (deferred).** Dependabot cannot sign commits. Strict enforcement would break automated dependency updates. Defer to 1.0 or when the project has a team that can manage signing keys. The branch protection changes above (enforce admins, block direct push) already protect against the main threats.

6. **Revisit at 1.0 or second maintainer.** When the project graduates from 0.x (ADR-0014) or gains a second maintainer, revisit: (a) flip `required_approving_review_count` to 1, (b) enable signed commit enforcement with a Dependabot-compatible signing setup (e.g., bot GPG key or squash-merge with maintainer signature).

## Considered Options

- **A. Enforce admins + code owners + dismiss stale, keep 0 reviews ✅** — chosen. Blocks direct push and admin bypass, enforces CODEOWNERS for external contributors, keeps solo self-merge working. Honest tradeoff on the "0 reviews" heuristic.

- **B. Full enforcement (1 required review + code owners + admins + signed commits) — rejected.** Maximum Scorecard points, but requires a second reviewer for every merge. For a solo project, this means either blocking all merges or rubber-stamping via a second account (security theater). Signed commit enforcement breaks Dependabot.

- **C. Leave as-is — rejected.** Accepts the Scorecard ding on branch protection. The "admin can push directly to main" gap is a real defense-in-depth weakness that costs nothing to close.

## Consequences

- **Direct push to `main` is blocked for everyone, including admins.** All changes go through PR. This is the single most important branch protection rule and it was already in place; decision 1 makes it airtight.

- **External contributor PRs require maintainer review (CODEOWNERS enforced).** This was already the intent (CODEOWNERS existed), but now it's enforced rather than advisory.

- **Solo maintainer can still self-merge.** With 0 required reviews, the maintainer can merge their own PRs. This is the deliberate tradeoff — the alternative (1 required review) is theater for a solo project.

- **Scorecard will note "0 required reviews" as a finding.** This is expected and documented here. The finding is honest: a solo project cannot require a second reviewer without a second person. The other branch protection heuristics (enforce admins, code owners, dismiss stale, force push blocked, linear history) will pass.

- **Signed commits are not enforced.** Scorecard's `Verified-Commits` check will note this. Documented as a deferred item with a clear trigger (1.0 / second maintainer). Dependabot compatibility is the blocking reason — not laziness.

- **Secret scanning + push protection enabled (separate setting, not branch protection).** GitHub's built-in secret scanning detects accidentally committed credentials (SQL connection strings with passwords, API keys) and blocks pushes containing them. Free for public repos. Directly relevant to a project that handles database credentials in `.env`. This is a repo setting, not a branch protection rule, but it's part of the same hardening pass.
