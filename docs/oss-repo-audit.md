# OSS Repo Audit — mssql-mcp

**Date:** 2026-07-21
**Scope:** Pre-public-launch benchmark of `mssql-mcp` against flagship OSS dev-tool repos.
**Method:** Anchored to real repos (ripgrep, bat, fzf, ast-grep, biome, oxc, eslint, typescript-eslint, playwright, bun, turbo, prisma, drizzle, dotnet/aspnetcore, microsoft/semantic-kernel, microsoft/agent-framework, reactiveui/ReactiveUI, devlooped/moq, dotnet/BenchmarkDotNet, open-telemetry/opentelemetry-dotnet, cilium/cilium, huggingface/transformers, electron/electron) and published standards (OpenSSF Scorecard, SLSA v1.0, OSPS Baseline, GitHub Attestations, Conventional Commits, Keep-a-Changelog, Contributor Covenant 2.1).

---

## Scoring summary

| # | Dimension | Score | Notes |
|---|---|---|---|
| 1 | README & onboarding | 7/10 | Strong prose; missing demo, comparison, key badges, ToC |
| 2 | Issue/PR templates | 1/10 | None present |
| 3 | CI/CD beyond build+test | 2/10 | ci.yml + release.yml only; no coverage/format/lint/provenance/SBOM/SARIF |
| 4 | Docs | 6/10 | SPEC + 16 ADRs is excellent; no rendered site, no versioning |
| 5 | Repo health signals | 1/10 | SECURITY.md only; no Scorecard/SLSA/Best Practices badge |
| 6 | DX patterns | 2/10 | Solution + npm wrapper; no devcontainer/mise/justfile/examples |
| 7 | Community | 5/10 | CONTRIBUTING is strong; no CoC, governance, CODEOWNERS |
| 8 | C#/.NET-specific | 2/10 | TreatWarningsAsErrors only; no CPM/sourcelink/analyzers/format/coverage |

**Overall: 26/80.** Solid foundations (SECURITY.md, ADRs, CONTRIBUTING, test taxonomy). Pre-launch gap is overwhelmingly in CI hardening, repo-health signals, and .NET tooling defaults — all addressable in <1 week.

---

## GAPS YOU HADN'T NOTICED (high-value, prioritized)

These are the gaps not in your original list. Each names what's missing, what to add, and a real reference.

### P0 — before public launch / 1.0.0

1. **`.editorconfig`** — root-level editor + `dotnet format` config. Without it, `dotnet format` has nothing to enforce and contributors fight over style. Reference: [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore/blob/main/.editorconfig), [microsoft/semantic-kernel](https://github.com/microsoft/semantic-kernel/blob/main/.editorconfig) — both ship a root `.editorconfig` with `[*]` charset/newline + `[*.cs]` dotnet_diagnostic severities.

2. **`.gitattributes`** — line-ending normalization (`* text=auto eol=lf`), `export-ignore` for `.github/`, `.codegraph/`, `.omo/`, `.devcontainer/` in `git archive` (used by `dotnet pack` source tarball), and `export-subst` for `$Format:%H$` version embedding. Reference: [dotnet/aspnetcore/.gitattributes](https://github.com/dotnet/aspnetcore/blob/main/.gitattributes), [dotnet/runtime/.gitattributes](https://github.com/dotnet/runtime/blob/main/.gitattributes).

3. **`global.json`** — pins SDK version with `"rollForward": "latestMinor"` so contributors get a compatible SDK. Reference: [dotnet/aspnetcore/global.json](https://github.com/dotnet/aspnetcore/blob/main/global.json), [microsoft/agent-framework/global.json](https://github.com/microsoft/agent-framework/blob/main/global.json), [dotnet/runtime/global.json](https://github.com/dotnet/runtime/blob/main/global.json).

4. **`.github/CODEOWNERS`** — GitHub auto-requests code owners as PR reviewers; required by OpenSSF Scorecard's Code-Review check and by any branch-protection rule that requires review. Even single-maintainer repos benefit (self-review fallback). Reference: [open-telemetry/opentelemetry-collector-contrib/.github/CODEOWNERS](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/.github/CODEOWNERS) (per-path ownership), [huggingface/transformers/.github/CODEOWNERS](https://github.com/huggingface/transformers/blob/main/.github/CODEOWNERS).

5. **SHA-pin all GitHub Actions** — `actions/checkout@v4` and `actions/setup-dotnet@v4` are tag-pinned. OpenSSF Scorecard `Dangerous-Workflow` check flags this; a compromised tag (tj-actions/changed-files Mar 2025 incident) compromises every consumer. Use SHA pins at the minor tag (e.g. `actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2`). Reference: [animatlabs .NET pipeline guide](https://animatlabs.com) (Mar 2026), [argoproj/argo-helm/.github/workflows/semantic.yml](https://github.com/argoproj/argo-helm/blob/main/.github/workflows/semantic.yml) (SHA-pinned + step-security/harden-runner).

6. **Top-level `permissions:` block in both workflows** — `ci.yml` has no `permissions:` at all (defaults to `GITHUB_TOKEN` write-permissive in older configs); `release.yml` sets `permissions: contents: write` at job level but no top-level read-restrictive default. OpenSSF Scorecard `Token-Permissions` check fails both. Add `permissions: contents: read` at workflow top, escalate per-job only where needed. Reference: [GitHub docs on token permissions](https://docs.github.com/en/actions/security-for-github-actions/security-guides/automatic-token-authentication), [Scorecard Token-Permissions check](https://github.com/ossf/scorecard/blob/main/docs/checks.md).

7. **`actions/attest-build-provenance@v2` in release.yml** — native GitHub build provenance (SLSA v1.0 Build Level 2) replaces the legacy `slsa-framework/slsa-github-generator` (which uploads `.intoto.jsonl` as a release asset — conflicts with GitHub's Mar 2026 immutable-release policy). Requires `permissions: id-token: write, attestations: write`. Verified with `gh attestation verify mssql-mcp.0.1.0.nupkg --repo codegiveness/mssql-mcp`. **Concrete .NET reference:** [pedrosakuma/dotnet-assembly-mcp commit 9bee953](https://github.com/pedrosakuma/dotnet-assembly-mcp/commit/9bee953) (May 2026) — attests nupkg + tar.gz/zip + GHCR image, ships a "Verifying releases" README section with `gh attestation verify` examples.

8. **npm publish with `--provenance`** — npm-native provenance is free and requires only `permissions: id-token: write`. The current `npm publish` step omits it. Reference: [npm docs on provenance](https://docs.npmjs.com/generating-provenance-statements), [GitHub blog on npm provenance](https://github.blog/security/supply-chain-security/introducing-npm-package-provenance). Combined with step 7, every artifact you ship (nupkg, archives, npm tarball) becomes verifiable.

9. **NuGet package metadata in `mssql-mcp.csproj`** — currently missing `<PackageLicenseExpression>MIT</PackageLicenseExpression>` (nuget.org shows "No license" without it), `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="..\README.md" Pack="true" PackagePath="\" />` (nuget.org README), `<RepositoryUrl>`, `<RepositoryType>`, `<RepositoryCommit>`, `<IncludeSymbols>true</IncludeSymbols>` + `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` (symbol packages for source debugging). Reference: [NuGet metadata best practices](https://learn.microsoft.com/nuget/create-packages/package-authoring-best-practice), [devlooped/moq/src/Directory.Build.props](https://github.com/devlooped/moq/blob/main/src/Directory.Build.props) (sets `PublishRepositoryUrl`/`GenerateRepositoryUrlAttribute`/`IncludeSymbols`).

10. **`.github/workflows/semantic.yml`** — `amannn/action-semantic-pull-request@v6.1.1` (SHA-pin: `48f256284bd46cdaab1048c3721360e808335d50`) validates PR titles match Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, etc.). Without this, auto-changelog (release-drafter, release-please, `gh release --generate-notes`) can't categorize. Reference: [electron/electron/.github/workflows/semantic.yml](https://github.com/electron/electron/blob/main/.github/workflows/semantic.yml), [nuxt/nuxt](https://github.com/nuxt/nuxt/blob/main/.github/workflows/semantic.yml) (with scopes config), [a2aproject/A2A](https://github.com/a2aproject/A2A) (conditional on changed paths).

11. **`.github/release-drafter.yml` + workflow** OR **`gh release create --generate-notes`** in release.yml — currently releases have no body. `release-drafter/release-drafter@v7` auto-maintains a draft release from conventional commits; or use `gh release create v0.1.0 --generate-notes` (GitHub-native, uses PR titles). Reference: [chartjs/Chart.js/.github/release-drafter.yml](https://github.com/chartjs/Chart.js/blob/main/.github/release-drafter.yml) (MIT), [hub4j/github-api](https://github.com/hub4j/github-api/blob/main/.github/release-drafter.yml) (MIT), [jenkinsci/docker](https://github.com/jenkinsci/docker/blob/main/.github/release-drafter.yml).

12. **`Directory.Build.props`** at repo root — centralizes: `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<AnalysisLevel>latest-recommended</AnalysisLevel>` (enables Microsoft's CAxxxx Roslyn analyzers — in-box, no extra package), `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`, `<ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>` (deterministic build paths for reproducible nupkg), `<PublishRepositoryUrl>true</PublishRepositoryUrl>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>` (sourcelink prereq). Reference: [microsoft/agent-framework/Directory.Build.props](https://github.com/microsoft/agent-framework/blob/main/Directory.Build.props), [microsoft/testfx](https://github.com/microsoft/testfx/blob/main/Directory.Build.props) + `Directory.Build.targets` validating version sync, [devlooped/moq/src/Directory.Build.props](https://github.com/devlooped/moq/blob/main/src/Directory.Build.props).

### P1 — within 30 days

13. **`.github/workflows/scorecard.yml`** — `ossf/scorecard-action@v2` runs on branch push + schedule, publishes results to the repo Security tab, enables the Scorecard badge in README: `[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/codegiveness/mssql-mcp/badge)](https://scorecard.dev/viewer/?uri=github.com/codegiveness/mssql-mcp)`. Reference: [OpenSSF Scorecard GitHub Action docs](https://github.com/ossf/scorecard-action), [cilium/cilium/.github/workflows/scorecards.yml](https://github.com/cilium/cilium/blob/main/.github/workflows/scorecards.yml).

14. **`.github/workflows/stale.yml`** — `actions/stale@v10.4.0` on cron, `days-before-issue-stale: 90`, `days-before-issue-close: 14`, `stale-issue-label: stale`. Essential once issues are public. Reference: [wojtekmaj/react-pdf/.github/workflows/stale.yml](https://github.com/wojtekmaj/react-pdf/blob/main/.github/workflows/stale.yml) (90/14, Monday cron), [huggingface/transformers](https://github.com/huggingface/transformers/blob/main/.github/workflows/stale.yml), [cilium/cilium](https://github.com/cilium/cilium/blob/main/.github/workflows/stale.yml).

15. **`.github/ISSUE_TEMPLATE/config.yml`** with `contact_links`** — beyond bug/feature templates, the config.yml adds "Report a security vulnerability" → SECURITY.md link and "Ask a question" → Discussions link, routing traffic away from issues. Reference: [nodejs/node/.github/ISSUE_TEMPLATE/config.yml](https://github.com/nodejs/node/blob/main/.github/ISSUE_TEMPLATE/config.yml).

16. **PR template with AI-generated disclosure** — 2025/2026 pattern: PR template asks "Does this PR contain AI-generated code?" checkbox, gating downstream review. Reference: [ros-navigation/navigation2/.github/PULL_REQUEST_TEMPLATE.md](https://github.com/ros-navigation/navigation2/blob/main/.github/PULL_REQUEST_TEMPLATE.md), [aws/aws-cdk](https://github.com/aws/aws-cdk/blob/main/.github/PULL_REQUEST_TEMPLATE.md) (Reason / Description / New permissions / Validation sections).

17. **`.config/dotnet-tools.json`** — repo manifest for dev tools (`dotnet tool restore`). Install CycloneDX, dotnet-format, Stryker here so contributors get them free. Reference: [CycloneDX dotnet tool docs](https://github.com/CycloneDX/cyclonedx-dotnet), standard .NET dev pattern.

18. **`.github/dependabot.yml` covering `github-actions` AND `nuget` ecosystems** — user noticed "no dependabot.yml" but it should cover both ecosystems (Actions SHA pins need weekly update; NuGet packages need monthly). Reference: [microsoft/agent-framework/.github/dependabot.yml](https://github.com/microsoft/agent-framework/blob/main/.github/dependabot.yml), [dotnet/aspnetcore/.github/dependabot.yml](https://github.com/dotnet/aspnetcore/blob/main/.github/dependabot.yml).

19. **`.github/workflows/codeql.yml`** — `github/codeql-action/init@v3` + `analyze@v3` for C# + JavaScript (npm/). Uploads SARIF to Security tab. Reference: [microsoft/agent-framework/.github/workflows/codeql.yml](https://github.com/microsoft/agent-framework/blob/main/.github/workflows/codeql.yml), [dotnet/extensions](https://github.com/dotnet/extensions/blob/main/.github/workflows/codeql.yml).

20. **README: Table of Contents** — README is 267 lines. No ToC navigation. Reference: [prisma/prisma/README.md](https://github.com/prisma/prisma/blob/main/README.md), [turbo-tools/turbo](https://github.com/vercel/turbo/blob/main/README.md), [biomejs/biome](https://github.com/biomejs/biome/blob/main/README.md) all ship ToC for READMEs >100 lines.

21. **README: comparison table** — "mssql-mcp vs raw `mssql` CLI vs mcp-server-sqlite vs generic database MCP". Frame the decision. Reference: [ast-grep/ast-grep README](https://github.com/ast-grep/ast-grep/blob/main/README.md) ("Why ast-grep" comparison), [biomejs/biome](https://github.com/biomejs/biome/blob/main/README.md) ("Biome vs Prettier + ESLint" table), [sharkdp/bat](https://github.com/sharkdp/bat/blob/main/README.md) (feature comparison with `cat`).

22. **README: demo** — asciinema cast or GIF of the install → connect → first query flow. Critical for MCP servers (buyers want to see it work before installing). Reference: [sharkdp/bat README asciinema](https://github.com/sharkdp/bat/blob/master/README.md), [junegunn/fzf README demo](https://github.com/junegunn/fzf/blob/master/README.md), [ast-grep](https://github.com/ast-grep/ast-grep/blob/main/README.md).

23. **`examples/` directory** with MCP client configs — `examples/claude-desktop.json`, `examples/cursor-mcp.json`, `examples/cline-mcp-settings.json`, `examples/continue.config.json`. MCP consumers copy-paste these. Reference: [modelcontextprotocol/servers](https://github.com/modelcontextprotocol/servers) ships per-server config snippets, [microsoft/azure-devops-mcp](https://github.com/microsoft/azure-devops-mcp) includes client setup examples.

24. **`CHANGELOG.md`** Keep-a-Changelog format — even if auto-generated downstream, the file should exist with an `## [Unreleased]` section so contributors can link to it. Reference: [Keep a Changelog](https://keepachangelog.com/), [ollama/ollama/CHANGELOG.md](https://github.com/ollama/ollama/blob/main/CHANGELOG.md), [biomejs/biome/CHANGELOG.md](https://github.com/biomejs/biome/blob/main/CHANGELOG.md).

25. **`CODE_OF_CONDUCT.md`** Contributor Covenant 2.1 — GitHub requires this file (not just a link) for the Community Profile checklist. Reference: [Contributor Covenant 2.1](https://www.contributor-covenant.org/version/2/1/code_of_conduct/), [dotnet/aspnetcore/CODE_OF_CONDUCT.md](https://github.com/dotnet/aspnetcore/blob/main/CODE_OF_CONDUCT.md) (uses .NET Foundation CoC, but Contributor Covenant is the OSS default).

26. **`.devcontainer/devcontainer.json`** — `image: mcr.microsoft.com/devcontainers/dotnet:1-10.0`, features: github-cli, docker-in-docker (for Azure SQL Edge integration tests), `postCreateCommand: dotnet build`. Reference: [microsoft/agent-framework/.devcontainer/dotnet/devcontainer.json](https://github.com/microsoft/agent-framework/blob/main/.devcontainer/dotnet/devcontainer.json), [MassTransit/MassTransit/.devcontainer](https://github.com/MassTransit/MassTransit/blob/dev/.devcontainer), [dotnet/aspnetcore/.devcontainer](https://github.com/dotnet/aspnetcore/blob/main/.devcontainer).

27. **`Directory.Packages.props`** (CPM) + `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` — removes `Version="..."` from all `<PackageReference>` entries; single source of truth for upgrades. Reference: [devlooped/moq/src/Directory.Packages.props](https://github.com/devlooped/moq/blob/main/src/Directory.Packages.props), [reactiveui/ReactiveUI/src/Directory.Packages.props](https://github.com/reactiveui/ReactiveUI/blob/main/src/Directory.Packages.props), [microsoft/agent-framework](https://github.com/microsoft/agent-framework/blob/main/Directory.Packages.props).

28. **`Microsoft.SourceLink.GitHub`** package + sourcelink config — `dotnet pack` embeds source-link metadata; consumers can step into mssql-mcp source in the debugger from NuGet. Reference: [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml/blob/main/Directory.Build.props) (SourceLink v1.1.1), [microsoft/msquic](https://github.com/microsoft/msquic) (v1.0.0), [Microsoft SourceLink docs](https://github.com/dotnet/sourcelink).

29. **`coverlet.collector` + `--collect:"XPlat Code Coverage"` in CI + Codecov upload** — .gitignore already has `*.coverage`/`*.coveragexml` (anticipated), but CI doesn't collect. Add `coverlet.collector` PackageReference to test projects, `dotnet test --collect:"XPlat Code Coverage"`, upload via `codecov/codecov-action@v4`. Reference: [open-telemetry/opentelemetry-dotnet/.github/workflows](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/.github/workflows), [Xabaril/AspNetCore.Diagnostics.HealthChecks](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks).

30. **`StyleCop.Analyzers`** + ruleset — enforces style rules at compile time (beyond what `dotnet format` enforces). Enable via `<PackageReference Include="StyleCop.Analyzers" PrivateAssets="all" />` + `<CodeAnalysisRuleSet>stylecop.ruleset</CodeAnalysisRuleSet>` or `<AnalysisLevel>latest-recommended</AnalysisLevel>`. Reference: [DotNetAnalyzers/StyleCopAnalyzers](https://github.com/DotNetAnalyzers/StyleCopAnalyzers), [microsoft/agent-framework](https://github.com/microsoft/agent-framework/blob/main/Directory.Build.props).

31. **`dotnet format --verify-no-changes` in CI** — separate workflow or step in ci.yml. Reference: [microsoft/semantic-kernel/.github/workflows/dotnet-format.yml](https://github.com/microsoft/semantic-kernel/blob/main/.github/workflows/dotnet-format.yml), [github/copilot-sdk](https://github.com/github/copilot-sdk) (inline in tests workflow), [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) (reusable `Lint - dotnet format` workflow).

32. **CycloneDX SBOM in release** — `dotnet tool install CycloneDX`, `dotnet CycloneDX mssql-mcp.sln --out sbom`, upload `bom.json` as release asset + attest it. Reference: [CycloneDX/cyclonedx-dotnet](https://github.com/CycloneDX/cyclonedx-dotnet), [cilium/cilium/.github/workflows](https://github.com/cilium/cilium/blob/main/.github/workflows) (combined SBOM+sign+attest), [azahar-emu/azahar](https://github.com/azahar-emu/azahar) (`if: github.ref_type == 'tag'`).

### P2 — nice-to-have

33. **OpenSSF Best Practices Badge** — separate from Scorecard. Self-certified at [bestpractices.dev](https://www.bestpractices.dev). 3 tiers (passing/silver/gold) + baseline-1/2/3. Badge: `[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/XXXX/badge)](https://www.bestpractices.dev/projects/XXXX)`. Reference: [OpenSSF Best Practices Badge Program](https://www.bestpractices.dev).

34. **Astro Starlight docs site** — render `docs/` (SPEC, ADRs, agents) as a searchable site. Starlight has Pagefind built-in (free search), ~50KB JS, MIT. Deploy to GitHub Pages. Reference: [Astro Starlight](https://starlight.astro.build/), [withastro/docs](https://github.com/withastro/docs) (Astro's own docs use Starlight). Alternative for versioned library docs: [Docusaurus](https://docusaurus.io/) (built-in versioning, Algolia DocSearch).

35. **All-contributors bot** — `.all-contributorsrc` + `@all-contributors please add @user for code,doc`. Badge: `https://img.shields.io/github/all-contributors/codegiveness/mssql-mcp/main`. Reference: [tensorchord/pgvecto.rs](https://github.com/tensorchord/pgvecto.rs/blob/main/README.md), [tensorchord/envd](https://github.com/tensorchord/envd/blob/main/README.md).

36. **BenchmarkDotNet project** — `tests/mssql-mcp.Benchmarks/` with `[MemoryDiagnoser]` on hot paths (Guard, SqlExecutor, TypeCoercion). Run on PRs touching `src/`, upload artifacts. Reference: [dotnet/BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet), [reactiveui/ReactiveUI/src/benchmarks](https://github.com/reactiveui/ReactiveUI/blob/main/src/benchmarks/Directory.Packages.props) (`<PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />`), [andrewlock/NetEscapades.EnumGenerators](https://github.com/andrewlock/NetEscapades.EnumGenerators).

37. **Stryker mutation testing** — `dotnet tool install dotnet-stryker`, run on Core project. Measures test effectiveness by mutating source. Reference: [stryker-mutator/stryker-net](https://github.com/stryker-mutator/stryker-net).

38. **`mise.toml`** / **`.tool-versions`** — pin dotnet version for non-.NET contributors using [mise](https://mise.jdx.dev/) or [asdf](https://asdf-vm.com/). Reference: [nrwl/nx/mise.toml](https://github.com/nrwl/nx/blob/main/mise.toml) (`[tools.dotnet] version = "9"`).

39. **`justfile`** or **`Makefile`** — `just test`, `just lint`, `just release`, `just integration`. Reference: [casey/just](https://github.com/casey/just), common in Rust dev-tool repos (ripgrep, fd, bat).

40. **`llms.txt`** in repo root — LLM-friendly docs index (2025 pattern). Reference: [llmstxt.org](https://llmstxt.org/), [Mintlify](https://mintlify.com/docs/llms-txt) popularized it.

41. **Signed commits (cosign/sigstore for commits, GPG classic)** — Scorecard `Signed-Commits` check. Not commonly enforced in .NET ecosystem; lower priority.

42. **Governance doc** — `GOVERNANCE.md` describing maintainer roles, decision process, how contributors become maintainers. Reference: [open-telemetry/community](https://github.com/open-telemetry/community), [kubernetes/community](https://github.com/kubernetes/community/blob/master/governance.md). Defer until multiple maintainers.

43. **SLSA Build Level 3** — requires reusable-workflow isolation (build runs in a reusable workflow called from the release workflow, no checkout of untrusted code in the build job). Reference: [SLSA v1.0 spec](https://slsa.dev/spec/v1.0/levels), [slsa-framework/github-builders](https://github.com/slsa-framework/github-builders).

44. **SARIF from analyzers uploaded to Security tab** — `github/codeql-action/upload-sarif@v3`. CodeQL handles this; StyleCop/Roslynator can also emit SARIF via `dotnet format --report sarif`. Reference: [GitHub code scanning SARIF docs](https://docs.github.com/en/code-security/code-scanning/integrating-with-code-scanning/uploading-a-sarif-file-to-github).

45. **`.github/FUNDING.yml`** — `github: codegiveness` (or whoever). Enables "Sponsor" button on repo. Reference: [GitHub sponsors docs](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/displaying-a-sponsor-button-in-your-repository).

46. **Branch protection + required status checks documented** — CONTRIBUTING.md should list protected branches (`main`), required status checks (ci, semantic-pr, codeql, dotnet-format), required reviews (1+), dismiss stale reviews. Reference: [GitHub branch protection docs](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-branches-in-your-repository/managing-a-branch-protection-rule).

47. **"good first issue" / "help wanted" labels** — predefined in `.github/labels.yml` or via [github/issue-labeler](https://github.com/github/issue-labeler). Reference: [kubernetes/kubernetes](https://github.com/kubernetes/kubernetes) labels, [microsoft/vscode](https://github.com/microsoft/vscode/blob/main/.github/labeler.yml).

---

## Per-dimension findings

### 1. README & onboarding — 7/10

**What top-tier repos do that mssql-mcp doesn't:**

- **Demo (asciinema/GIF)** — [sharkdp/bat](https://github.com/sharkdp/bat/blob/master/README.md), [junegunn/fzf](https://github.com/junegunn/fzf/blob/master/README.md), [ast-grep/ast-grep](https://github.com/ast-grep/ast-grep/blob/main/README.md) all lead with a visual demo in the first screenful. mssql-mcp has none.
- **Comparison table** — [biomejs/biome](https://github.com/biomejs/biome/blob/main/README.md) ("Biome vs Prettier + ESLint"), [ast-grep](https://github.com/ast-grep/ast-grep/blob/main/README.md) ("Why ast-grep"). mssql-mcp doesn't position itself vs raw `sqlcmd` or other DB MCPs.
- **Badges** — mssql-mcp has 5 (CI, NuGet, npm, .NET 10, MIT). Missing: OpenSSF Scorecard, OpenSSF Best Practices, coverage, all-contributors, downloads count, "what is MCP" link badge. Reference: [biomejs/biome README badge row](https://github.com/biomejs/biome/blob/main/README.md), [turbo-tools/turbo](https://github.com/vercel/turbo/blob/main/README.md).
- **Table of Contents** — README is 267 lines, no ToC. [prisma/prisma](https://github.com/prisma/prisma/blob/main/README.md), [vercel/turbo](https://github.com/vercel/turbo/blob/main/README.md) ship ToC for long READMEs.
- **"Who uses this" / adopters** — common in dev-tool READMEs. Defer until adopters exist.

**What mssql-mcp does well:** quickstart covers both npm and dotnet install, modes/tools/auth/config tables, install/platform matrix, security/guard layers, dev setup. README is genuinely strong.

### 2. Issue & PR templates — 1/10

**Missing entirely.** Add:

- `.github/ISSUE_TEMPLATE/bug-report.yml` (YAML form, not markdown) — `name: 🐛 Bug report`, `labels: [bug, needs-triage]`, body with markdown/textarea/checkboxes/dropdown types. Reference: [nodejs/node/.github/ISSUE_TEMPLATE/1-bug-report.yml](https://github.com/nodejs/node/blob/main/.github/ISSUE_TEMPLATE/1-bug-report.yml), [TryGhost/Ghost](https://github.com/TryGhost/Ghost/blob/main/.github/ISSUE_TEMPLATE/bug-report.yml), [remix-run/react-router](https://github.com/remix-run/react-router/blob/main/.github/ISSUE_TEMPLATE/bug_report.yml).
- `.github/ISSUE_TEMPLATE/feature-request.yml`. Reference: same repos.
- `.github/ISSUE_TEMPLATE/config.yml` — `blank_issues_enabled: false`, `contact_links:` for Security (→ SECURITY.md) and Discussions.
- `.github/PULL_REQUEST_TEMPLATE.md` — `## Description`, `Fixes #`, `## Type of change` (checkboxes), `## Checklist`, `## AI-generated disclosure` (2025 pattern). Reference: [aws/aws-cdk](https://github.com/aws/aws-cdk/blob/main/.github/PULL_REQUEST_TEMPLATE.md), [ros-navigation/navigation2](https://github.com/ros-navigation/navigation2/blob/main/.github/PULL_REQUEST_TEMPLATE.md).

### 3. CI/CD beyond build+test — 2/10

**Current:** ci.yml (ubuntu, build/test/pack), release.yml (5 RIDs + archives + sha256 + GH Release + NuGet push + npm publish).

**Missing (P0):** top-level `permissions:` blocks, SHA-pinned actions, `actions/attest-build-provenance@v2`, npm `--provenance`, `gh release create --generate-notes`.

**Missing (P1):** CodeQL workflow, OpenSSF Scorecard workflow, `dotnet format --verify-no-changes`, coverlet coverage upload to Codecov, CycloneDX SBOM in release, stale-action workflow.

**Missing (P2):** SLSA Level 3 (reusable workflow), SARIF from analyzers, branch protection docs.

**Reference for the target state:** [pedrosakuma/dotnet-assembly-mcp commit 9bee953](https://github.com/pedrosakuma/dotnet-assembly-mcp/commit/9bee953) (May 2026) — full .NET pipeline with provenance attestation for nupkg + archives + GHCR image + README "Verifying releases" section. This is the closest peer to mssql-mcp.

### 4. Docs — 6/10

**Strong:** SPEC-v1.md (323 lines), 16 ADRs (0001–0016), agents/ docs for issue tracker/domain/triage-labels.

**Missing (P1):** rendered docs site (Astro Starlight recommended — Pagefind free search, MIT, ~50KB JS). Render the existing ADRs/SPEC as a searchable site. Reference: [withastro/docs](https://github.com/withastro/docs) (Astro's own docs use Starlight), [distr.sh](https://github.com/distr-sh/distr) (migrated Docusaurus → Starlight).

**Missing (P2):** versioned docs (deferred — single version at 0.x), `llms.txt` for LLM-friendly docs index, ADR template file (ADR-0000 placeholder).

### 5. Repo health signals — 1/10

**Current:** SECURITY.md (good), LICENSE (MIT), NOTICE (trademark).

**Missing (P0):** `actions/attest-build-provenance@v2` (native GitHub provenance), SHA-pinned Actions, top-level `permissions:` blocks.

**Missing (P1):** OpenSSF Scorecard workflow + badge, Dependabot (github-actions + nuget ecosystems).

**Missing (P2):** OpenSSF Best Practices Badge (self-certified, bestpractices.dev), signed commits, SLSA Level 3, All-contributors.

### 6. DX patterns — 2/10

**Missing (P0):** `.editorconfig`, `.gitattributes`, `global.json`, `Directory.Build.props`.

**Missing (P1):** `.devcontainer/devcontainer.json`, `.config/dotnet-tools.json` (repo tool manifest), `examples/` with MCP client configs (claude-desktop.json, cursor-mcp.json, cline-mcp-settings.json, continue.config.json).

**Missing (P2):** `mise.toml` / `.tool-versions`, `justfile`/`Makefile`, nix flake.

### 7. Community — 5/10

**Strong:** CONTRIBUTING.md (152 lines: setup, layout, testing, C# conventions, ADR workflow, commit conventions, branch naming, PR checklist, merge strategy). SECURITY.md with SLA table.

**Missing (P0):** `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1), `.github/CODEOWNERS`.

**Missing (P1):** `.github/FUNDING.yml`, stale-action workflow, semantic-PR workflow, "good first issue"/"help wanted" label taxonomy.

**Missing (P2):** governance doc (defer until multiple maintainers).

### 8. C#/.NET-specific — 2/10

**Current:** `TreatWarningsAsErrors=true` in main csproj, .NET 10, MCP SDK 1.4.1.

**Missing (P0):** `Directory.Build.props` (centralize properties + `<AnalysisLevel>latest-recommended</AnalysisLevel>` for built-in Roslyn analyzers + `<ContinuousIntegrationBuild>` for deterministic builds), `Microsoft.SourceLink.GitHub` package, NuGet metadata (`<PackageLicenseExpression>`, `<PackageReadmeFile>`, `<RepositoryUrl>`, `<IncludeSymbols>`+`<SymbolPackageFormat>snupkg`).

**Missing (P1):** `Directory.Packages.props` (CPM), `StyleCop.Analyzers`, `dotnet format --verify-no-changes` in CI, `coverlet.collector` + Codecov, CycloneDX SBOM, `.config/dotnet-tools.json`.

**Missing (P2):** BenchmarkDotNet project, Stryker mutation testing, Roslynator add-on.

**Reference for the target .NET repo state:** [microsoft/agent-framework](https://github.com/microsoft/agent-framework) — ships `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `.devcontainer/dotnet/`, CodeQL, dotnet-format, Dependabot. Closest "exemplary .NET OSS repo" in 2026.

---

## Consolidated priority list

### P0 — before public launch / 1.0.0 (12 items, ~1 day of work)

1. `.editorconfig` (root)
2. `.gitattributes` (root)
3. `global.json` (pin SDK + rollForward)
4. `.github/CODEOWNERS`
5. SHA-pin all GitHub Actions in ci.yml + release.yml
6. Top-level `permissions: contents: read` in both workflows
7. `actions/attest-build-provenance@v2` in release.yml (nupkg + archives)
8. `npm publish --provenance` in release.yml
9. NuGet package metadata in mssql-mcp.csproj (license expression, README, repository URL, symbols/snupkg)
10. `.github/workflows/semantic.yml` (amannn/action-semantic-pull-request)
11. `.github/release-drafter.yml` + workflow OR `gh release create --generate-notes`
12. `Directory.Build.props` at repo root (AnalysisLevel, ContinuousIntegrationBuild, TreatWarningsAsErrors, SourceLink props)

### P1 — within 30 days (20 items)

13. `.github/workflows/codeql.yml`
14. `.github/workflows/scorecard.yml` + badge in README
15. `.github/workflows/stale.yml`
16. `.github/ISSUE_TEMPLATE/` (bug-report.yml, feature-request.yml, config.yml)
17. `.github/PULL_REQUEST_TEMPLATE.md` (with AI-generated disclosure)
18. `.github/dependabot.yml` (github-actions + nuget ecosystems)
19. `.github/FUNDING.yml`
20. `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1)
21. `CHANGELOG.md` (Keep-a-Changelog, `## [Unreleased]` section)
22. `.devcontainer/devcontainer.json`
23. `.config/dotnet-tools.json` (CycloneDX, format, Stryker)
24. `examples/` (claude-desktop.json, cursor-mcp.json, cline-mcp-settings.json, continue.config.json)
25. `Directory.Packages.props` (CPM)
26. `Microsoft.SourceLink.GitHub` package
27. `StyleCop.Analyzers` + ruleset
28. `coverlet.collector` + Codecov upload + badge
29. `dotnet format --verify-no-changes` in CI
30. CycloneDX SBOM in release (bom.json as asset + attested)
31. README: ToC, demo gif/asciinema, comparison table, badges (Scorecard, coverage, all-contributors)
32. Branch protection + required status checks documented in CONTRIBUTING

### P2 — nice-to-have (13 items)

33. OpenSSF Best Practices Badge (bestpractices.dev)
34. Astro Starlight docs site
35. All-contributors bot
36. BenchmarkDotNet project
37. Stryker mutation testing
38. `mise.toml` / `.tool-versions`
39. `justfile` or `Makefile`
40. `llms.txt`
41. Signed commits
42. Governance doc
43. SLSA Build Level 3
44. SARIF from analyzers to Security tab
45. "good first issue" / "help wanted" label taxonomy
46. "Who uses this" / adopters section

---

## Reference repos index

**.NET exemplars (closest to mssql-mcp):**
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework) — Directory.Build.props, CPM, global.json, .editorconfig, .devcontainer, CodeQL, dotnet-format, Dependabot. **Best single reference.**
- [microsoft/semantic-kernel](https://github.com/microsoft/semantic-kernel) — dotnet-format in docker, analyzers.
- [devlooped/moq](https://github.com/devlooped/moq) — Directory.Build.props + CPM + sourcelink.
- [pedrosakuma/dotnet-assembly-mcp](https://github.com/pedrosakuma/dotnet-assembly-mcp) — .NET MCP server with full provenance attestation (May 2026). **Closest peer.**
- [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) — reusable format workflow, coverage.
- [reactiveui/ReactiveUI](https://github.com/reactiveui/ReactiveUI) — CPM + BenchmarkDotNet.

**Dev-tool README exemplars:**
- [sharkdp/bat](https://github.com/sharkdp/bat), [junegunn/fzf](https://github.com/junegunn/fzf), [ast-grep/ast-grep](https://github.com/ast-grep/ast-grep), [biomejs/biome](https://github.com/biomejs/biome), [prisma/prisma](https://github.com/prisma/prisma), [vercel/turbo](https://github.com/vercel/turbo).

**Security/supply-chain exemplars:**
- [cilium/cilium](https://github.com/cilium/cilium) — combined SBOM + sign + attest, Scorecard, stale.
- [argoproj/argo-helm](https://github.com/argoproj/argo-helm) — SHA-pinned actions + harden-runner + semantic-PR.

**Published standards:**
- [OpenSSF Scorecard](https://github.com/ossf/scorecard) — 18 checks, Scorecard GitHub Action.
- [SLSA v1.0](https://slsa.dev/spec/v1.0/levels) — build levels.
- [GitHub Attestations](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds) — `actions/attest-build-provenance`.
- [Conventional Commits](https://www.conventionalcommits.org/) — PR title format.
- [Keep a Changelog](https://keepachangelog.com/) — CHANGELOG format.
- [Contributor Covenant 2.1](https://www.contributor-covenant.org/version/2/1/code_of_conduct/) — CoC.
- [OpenSSF Best Practices Badge](https://www.bestpractices.dev) — self-certified badge.
