# Security and quality follow-up

This is a current disposition and verification checklist, not a rewrite of the historical security audits. It does not authorize a 1.x release. A configured control is not a successful execution, and a Scorecard score is not a vulnerability assessment.

## Baseline evidence and twelve active findings

Repository API inspection reported **12 open code-scanning alerts**, all produced by **OpenSSF Scorecard v5.5.0 on `main` at `d625a6b`**. It also reported zero open Dependabot vulnerability alerts, zero open secret-scanning alerts, and secret scanning, push protection, and dependency security updates enabled. Those are point-in-time observations. They do not establish that this working branch is free of vulnerabilities.

The alert numbers below identify the existing GitHub findings, not hypothetical new issues. Original line numbers belong to the scanned revision and will move with edits.

| Group | Alerts and original evidence | Disposition and required proof |
|---|---|---|
| Container dependencies | **#3** SDK image and **#4** runtime-deps image were tag-only Dockerfile references | Docker images now use registry digests, retaining .NET 10. Verify a real Docker build and executable smoke run, then rerun Scorecard against the merged revision. A digest does not establish image vulnerability status. |
| NuGet restore graphs | **#5** Dockerfile restore/publish, **#6** CI build (line 25), **#15** release publish (line 74) | Central versions plus generated lock files pin the actual direct/transitive resolved graph and content hashes. Bare central versions are minimum ranges, not exact pins by themselves. Shared props enable locks; explicit runtime restores select separate profile lock paths. CI/release/Docker use locked restore followed by no-restore build/publish/pack. Generate and commit every graph and exercise the workflow/Docker SDKs. Fresh merged-main Scorecard marked these original findings fixed; that is not a package-vulnerability assessment. |
| Local npm package smoke installs | **#7** CI line 152 and **#8** CI line 144 flag `npm install ... ./*.tgz` | The arguments are local archive paths, not an unversioned registry tool. CI builds the native binary, stages it in the platform package, packs both manifests, and copies those packs into isolated install directories. Main manifest optional dependencies specify exact versions. An inspected stale platform manifest (`0.5.2` versus main `0.5.5`) meant the old source alone was insufficient proof of registry independence. All five platform stamps now belong to the authoritative sync/version-consistency scripts, rather than a CI-only workaround. CI installs with `--offline --ignore-scripts` and invokes the installed local executable directly, avoiding `npx` registry fallback. Successful offline installs, inspection of `file:` archive resolutions, and execution of the staged binary are required false-positive evidence. Do not dismiss either alert merely from its command spelling. |
| Static analysis | **#10** SAST reported 0 of 27 recent commits checked | A real CodeQL workflow is configured for C# and JavaScript/TypeScript on pushes to `main`, PRs to `main`, and weekly. Init/analyze are pinned to `2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2` (v4.38.2), with `security-extended` queries. Require successful extraction, diagnostic review, and uploaded analyses for both languages. Historical unchecked commits cannot be retroactively made scanned by adding a workflow. |
| Fuzzing | **#11** no integrated fuzzing detected | A development-only SharpFuzz/libFuzzer target and bounded CI campaign are implemented. Both ScriptDom and Guard are instrumented; the target exposes unexpected parser exceptions and checks accepted SQL for writes. Require observed managed coverage, mutated executions, a successful intentional-crash detection probe, and uploaded real campaign artifacts before settling the finding. Deterministic regression tests are not presented as fuzzing. |
| Maintainer history | **#12** repository younger than 90 days; **#13** Code-Review reported 0 of 22 approved changes | **Residual constraints.** Time and sustained genuine maintenance must satisfy the age heuristic. ADR-0033 deliberately keeps zero required approving reviews for the solo maintainer. A second qualified person and actual independent PR reviews are required to improve review history. Do not fabricate age, backdate activity, or rubber-stamp with a second account. |
| External best-practices badge | **#14** CII-Best-Practices badge score 0 | **Human/external prerequisite.** The existing self-assessment and closed historical issue #68 are not a granted badge. A human must verify the claims, submit to bestpractices.dev, and obtain the service's actual result. Do not claim passing or blindly reopen the historical documentation issue as though submission already happened. |

No alert is automatically dismissed by these edits. GitHub disposition must follow observed verification and a fresh scan of the merged code. Keep external prerequisites visible even if a tool stops detecting them.

## Merged-main findings and disposition

The [merged-main Scorecard run](https://github.com/codegiveness/mssql-mcp/actions/runs/36962352637) scanned `194be9b` and completed successfully. It automatically marked the seven original dependency findings **#3–#8 and #15** fixed. Its **6.5 score is not a security guarantee**. The original SAST/Fuzzing/history/review/badge findings remained visible, and the moved local-archive npm commands generated **#16/#17**.

The [merged-main CodeQL run](https://github.com/codegiveness/mssql-mcp/actions/runs/36962352592) uploaded **four JavaScript and two C# findings**, despite successful PR checks. This is why merge-main scan inspection is a separate gate:

- **#18–#21, insecure temporary test files:** a throwaway symlink attack against the predictable shared-temp manifest path overwrote its target while the fixture suite still passed. Fixture copies and malformed inputs now live in one atomically created `mkdtempSync` directory. The same attack left the target unchanged after the fix; observed permissions were `0700`, the real suite passed, and the exit handler removed the owned directory. Incidental newline/line-count assertions were removed rather than re-pinned.
- **#22, retry test:** the flagged connection was never opened. The test only assigned a provider and asserted the same object, without proving executor behavior; that tautology and its companion event-hook wiring assertion were removed. Meaningful retry-option and live SQL checks remain.
- **#23, connection validation:** downloaded SARIF traced every reported flow to test fixtures: invalid-keyword parsing, nonexistent reserved `.example` hosts, or a pre-cancelled localhost call. No trace established unencrypted application traffic. A throwaway program using the actual pinned SqlClient compared omitted `Encrypt` with `SqlConnectionEncryptOption.Mandatory` successfully (its display string is `True`) and observed a closed constructed connection. This is an evidence-backed false-positive disposition for these traces, not a blanket claim about every operator's connection string. Explicit operator TLS choices remain unchanged.
- **#16/#17, local npm archives:** the commands install only archives built from the checked-out source with `--offline --ignore-scripts`; the matching native artifact was actually installed and executed. No unpinned registry download or lifecycle script is permitted. The command-name heuristic is a false positive for this bounded smoke path, not evidence that all npm supply-chain risks disappear.
- **#11, custom fuzz integration:** observed local and hosted SharpFuzz/libFuzzer campaigns and the managed crash probe demonstrate genuine fuzz execution. Scorecard does not recognize this custom integration; retain campaign evidence when marking this finding false positive.
- **#10/#12/#13/#14 remain genuine residuals:** historical commits lack SAST coverage, repository age/history has not met the heuristic, no independent approvals were observed, and the external badge is not awarded. Future scan execution, real maintenance/review history, and human submission—not cosmetic dismissal—settle them.

[PR #146](https://github.com/codegiveness/mssql-mcp/pull/146) merged the confirmed fix. Its [fresh-main CodeQL run](https://github.com/codegiveness/mssql-mcp/actions/runs/36963648723) uploaded zero JavaScript findings and one C# finding (the individually classified test-only #23), with empty errors/warnings. GitHub marked **#18–#22 fixed**. **#23** is dismissed as `used in tests`; **#11/#16/#17** are dismissed as `false positive`, each with its specific evidence. **#10/#12/#13/#14** remain open. No queries or test files were excluded.

Follow-up local acceptance: **437 unit / 462 full live-SQL tests passed**, zero failures and four existing unreachable-case skips. The two-test reduction removes the described retry-wiring assertions, not failing behavior checks. Locked restore/build had zero warnings/errors; actual changed-file C# LSP diagnostics had zero errors. CLI validate/help/unknown-argument, format, version/release guards, README/npm/redirect-cache checks and the official **9-tool MCP stdio smoke** passed.

The subsequent dependency PRs **#142–#145** are integrated together: Test SDK **18.10.1**, SourceLink **10.0.401**, ScriptDom **180.117.0**, and xunit.v3 **4.0.1**. Canonical-registry regeneration covers portable, fuzz and all six runtime graphs, not only the partial lock updates in individual bot branches. The updated build and **437 unit / 462 live-SQL** tests passed; formatting, CLI validation, official MCP smoke and release/version/npm checks passed. All six profiles passed locked restore/publish and NuGet pack; only linux-x64 and the Alpine container were executed. The published native server returned SQL answers **42/43**, rejected a write, refused oversized raw XML and remained usable. Container SQL validation passed; its build emitted two nonfatal SourceLink metadata warnings because `.git` is excluded from the build context. A fresh ScriptDom/Guard campaign completed **55,217 inputs in 11 seconds**, features **3,182→14,400**, corpus **4→865**, with an empty product-findings directory and a successful separate crash probe. This remains bounded evidence.

## CodeQL support and security boundary

[GitHub's official build-options documentation](https://docs.github.com/en/code-security/reference/code-scanning/codeql/build-options-for-compiled-languages#building-c) explicitly supports C# `build-mode: none`: the extractor restores dependencies, generates selected sources, and analyzes repository source without a normal project build. The pinned [init action definition](https://github.com/github/codeql-action/blob/2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2/init/action.yml) supports `none` for interpreted languages too. C# first restores the solution in locked mode; `RestoreLockedMode=true` is also passed through the job environment for extractor restores.

The workflow uses `pull_request`, **not** `pull_request_target`. It has no publishing, repository-content write, OIDC, or secret permissions. Only the analysis job receives `security-events: write`, alongside `contents: read`; checkout does not persist credentials. GitHub applies its fork-PR token restrictions. Do not enable elevated fork permissions or add secrets to make a scan pass.

No-build extraction has documented accuracy limitations, including unresolved dependencies, colliding class names, and generated source. This repository uses generated regex and JSON serialization code; scanning hand-written source does not prove all generated runtime paths were covered. Inspect CodeQL diagnostics and switch to a supported traced manual build if extraction is incomplete. The built-in .NET analyzer configuration and six intentional suppressions in ADR-0020 remain separate from this security scan.

Observed [PR #139 CodeQL run](https://github.com/codegiveness/mssql-mcp/actions/runs/36960543627): both languages completed analysis and uploaded SARIF. The C# extraction log recorded **283 resolved / 0 unresolved references**. Uploaded API records reported **0 results, empty error/warning fields**, with 63 C# and 103 JavaScript/TypeScript rules. These observations support a working scan, not a guarantee against vulnerabilities or unmodeled flows.

## Generate and verify genuine NuGet locks

[NuGet's lock-file documentation](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies) describes the content-hashed dependency closure, locked restore, and custom `NuGetLockFilePath`. Exact central pins are necessary but do not themselves lock transitive resolution. Never hand-author content hashes.

Shared props enable lock generation; the restore commands select the graph explicitly:

- Portable graph: `<project directory>/packages.lock.json`.
- Runtime publish graph: `<project directory>/packages.<RID>.lock.json`, selected with `--lock-file-path packages.<RID>.lock.json`. The commands below supply exactly one RID and a relative per-project filename per profile; an evaluated MSBuild property alone did not select the intended path in the observed CLI restores.
- Runtime files are generated for the app **and each referenced project** under the same restore profile. Do not commit only the app lock and leave its references unlocked.

After dependency pin changes, use the intended .NET 10 SDK and generate the following graphs from the repository root. `--force-evaluate` is only for intentional regeneration; never combine it with `--locked-mode`.

Generate locks from an empty package cache and the canonical registry. The Ubuntu SDK's implicit `library-packs` feed supplied a repackaged `Microsoft.NET.ILLink.Tasks.10.0.12` with a different SHA512 from nuget.org; the first Docker locked restore correctly rejected it with NU1403. Shared props disable that implicit feed, and regeneration uses a fresh cache. If an old cache still contains the repackaged artifact, use a fresh `NUGET_PACKAGES` directory; do not disable content-hash validation or rewrite locks to accommodate it.

```sh
export NUGET_PACKAGES="$(mktemp -d)"
dotnet restore mssql-mcp.sln --force-evaluate --source https://api.nuget.org/v3/index.json

for rid in linux-x64 linux-arm64 osx-x64 osx-arm64 linux-musl-x64; do
  dotnet restore src/mssql-mcp/mssql-mcp.csproj -r "$rid" \
    -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=true \
    --lock-file-path "packages.$rid.lock.json" --force-evaluate \
    --source https://api.nuget.org/v3/index.json
done

dotnet restore src/mssql-mcp/mssql-mcp.csproj -r win-x64 \
  -p:SelfContained=false -p:PublishSingleFile=true -p:PublishTrimmed=false \
  --lock-file-path packages.win-x64.lock.json --force-evaluate \
  --source https://api.nuget.org/v3/index.json
```

The app paths are `src/mssql-mcp/packages.lock.json` and `src/mssql-mcp/packages.{linux-x64,linux-arm64,osx-x64,osx-arm64,linux-musl-x64,win-x64}.lock.json` (the braces here enumerate filenames). Referenced Core and Tools projects have their own corresponding files. Tests have portable locks from the solution restore. Review resolved versions and sources, then commit the actual generated locks together with the pins. A bare version such as `1.2.3` in PackageReference is a minimum, not an exact range; central exact pins use `[1.2.3]`.

Verification must repeat each profile **without** regeneration:

```sh
dotnet restore mssql-mcp.sln --locked-mode
dotnet build mssql-mcp.sln --no-restore

for rid in linux-x64 linux-arm64 osx-x64 osx-arm64 linux-musl-x64; do
  dotnet restore src/mssql-mcp/mssql-mcp.csproj -r "$rid" \
    -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=true \
    --lock-file-path "packages.$rid.lock.json" --locked-mode
  dotnet publish src/mssql-mcp -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:PublishTrimmed=true --no-restore \
    -o "/tmp/mssql-mcp-locked/$rid"
done

dotnet restore src/mssql-mcp/mssql-mcp.csproj -r win-x64 \
  -p:SelfContained=false -p:PublishSingleFile=true -p:PublishTrimmed=false \
  --lock-file-path packages.win-x64.lock.json --locked-mode
dotnet publish src/mssql-mcp -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:PublishTrimmed=false --no-restore -o /tmp/mssql-mcp-locked/win-x64

# A RID restore replaces obj/project.assets.json. Restore portable assets before pack.
dotnet restore src/mssql-mcp/mssql-mcp.csproj --locked-mode
dotnet pack src/mssql-mcp -c Release --no-restore -o /tmp/mssql-mcp-locked/nupkg
```

Run `--version` and `--help` on the native executable for the verification host; cross-publishing is not proof that other operating systems execute correctly. Build and run the Docker image too: it uses the `linux-musl-x64` trimmed self-contained graph. SDK-provided runtime/ILLink package versions can differ between local SDKs, the digest-pinned Docker SDK, and CI's `10.0.x`; a locked restore failure is a real incompatibility to resolve, not a reason to turn locked mode off. Verify with those actual SDKs before publication. Tool-manifest restore for the pinned SBOM generator is a separate tool installation, not protected by project `packages.lock.json`.

## Evidence for the local npm findings

The relevant source is `.github/workflows/ci.yml`, `npm/package.json`, and `npm/platforms/linux-x64/package.json`. Reproduce the npm-pack job using the just-published linux-x64 binary, not a downloaded registry binary. Both install directories must be fresh. After the first offline install, inspect `package-lock.json` and run `npm ls --all --json`; the main and applicable linux-x64 platform package must resolve from the copied local archives and have matching versions. Invoke `./node_modules/.bin/mssql-mcp --version` and compare it to the built executable's version. After the main-only offline install with optional packages disabled, run the same local executable with `MSSQL_MCP_NO_DOWNLOAD=1` and verify the documented download/alternative-install error. These observations distinguish a local-package smoke install from installing an unpinned external tool. Merely adding an npm lockfile unrelated to these ephemeral packs would not prove the tested artifact's identity.

## Coverage-guided parser and Guard fuzzing

The nonshipping project `fuzz/mssql-mcp.Fuzz` references Core and exact-pinned SharpFuzz 2.3.0, but is not part of the release tool's dependency graph or the solution's shipping builds. [SharpFuzz's official libFuzzer instructions](https://github.com/Metalnem/sharpfuzz/blob/master/docs/libFuzzer.md) document `Fuzzer.LibFuzzer.Run`, native bridge compilation, and instrumenting managed assemblies. The [upstream driver](https://github.com/Metalnem/sharpfuzz/blob/master/scripts/fuzz-libfuzzer.ps1) documents the `--target_path=dotnet --target_arg=<harness.dll>` invocation.

`scripts/run-fuzz.sh` performs locked restore and publish, installs the exact instrumentation tool version 2.3.0, and instruments Core's Guard types plus ScriptDom. It downloads the native libFuzzer bridge from commit `bd39d4e88d715ab460a929943645be2a186cde52`, verifies SHA256 `90f019e2e9ad3a0b93c7ecc2c5afb2fbfc8b5aab6aac51c7e0d349ec79354f36`, and compiles it with `clang++ -fsanitize=fuzzer`. This follows the supported native bridge rather than substituting hand-written random mutation or source markers.

The callback parses input directly before invoking `SqlGuard.ValidateStrict` because Guard catches parser exceptions; unexpected parser exceptions must remain visible to libFuzzer. Ordinary ScriptDom parse-error results are valid outcomes. If Guard accepts a batch, a separate visitor asserts that all statements are SELECT and none has an INTO target. The harness never opens a SQL connection. Four seed files cover valid SELECT, CTE/Unicode/quoted values, nested writes/batch separators, and malformed comments/strings; `fuzz/sql.dict` guides mutation.

The runner first uses `MSSQL_FUZZ_CRASH_PROBE=1` for a separate deliberate managed exception and requires both a failed engine run and its crash artifact/error message. That artifact validates the engine, **not a product defect**. The actual campaign explicitly removes this variable, fixes the mutation seed to 1, limits input to 4096 bytes, uses a five-second per-input timeout, and runs for 60 seconds in CI (local duration accepts 1–120 seconds). An outer timeout bounds an unresponsive native bridge. Failures are not swallowed; real crashes/timeouts remain campaign failures with artifacts. The wrapper's limits are not a total managed-process memory guarantee.

Generate the harness's real portable lock separately after pin changes because the project is deliberately outside the main solution:

```sh
dotnet restore fuzz/mssql-mcp.Fuzz/mssql-mcp.Fuzz.csproj --force-evaluate

# Requires .NET 10, clang++/compiler-rt libFuzzer, curl, and GNU timeout.
# Use a new directory; the runner refuses to overwrite existing campaign output.
bash scripts/run-fuzz.sh /tmp/mssql-mcp-fuzz-proof 60
```

The output directory retains `instrumentation.log`, `crash-probe.log`, `campaign.log`, the mutated `corpus`, separately labeled `probe` artifacts, and real `findings`. Review the instrumentation counts, libFuzzer coverage/features/executions, corpus evolution, and crash status; a successful process exit by itself is insufficient proof of managed coverage. `.github/workflows/fuzz.yml` runs the 60-second campaign on `main`, PRs to `main`, and weekly under a ten-minute job timeout, uploads those artifacts even on failure, and grants only `contents: read` with no secrets or elevated PR permissions.

This describes implemented behavior, **not an unobserved passing campaign**. Require a real run and hosted workflow evidence, minimize any findings into regression cases, and inspect a fresh Scorecard result before calling #11 settled.

### Local execution evidence (2026-10-02)

`bash scripts/run-fuzz.sh /tmp/mssql-mcp-fuzz-proof-20261002 10` completed after genuine instrumentation and native bridge compilation. The separate intentional managed exception produced the expected crash artifact. The real campaign executed **52,661 inputs in 11 seconds**, registered the 65,536-entry managed counter bitmap, increased feature count from **3,191 to 14,456**, and grew the corpus from **4 to 867 entries**. Its real findings directory was empty and the runner exited zero. Native `cov`/RSS counters are not a claim about total managed coverage or process memory. This is a bounded local campaign, not exhaustive proof or a substitute for hosted runs.

Observed [hosted fuzz run](https://github.com/codegiveness/mssql-mcp/actions/runs/36960543729): the separate managed crash probe succeeded; the real campaign completed **370,328 inputs in 61 seconds**, increased features from **3,191 to 22,137**, and expanded the corpus from **4 to 1,706 entries**. The successful runner and downloaded artifacts contained no emitted product findings. Input-size/time limits and incomplete exploration still apply.


## Local acceptance evidence (2026-10-02)

- Portable locked restore and build completed with zero warnings/errors. Non-integration tests: **439 passed, 0 failed, 4 existing unreachable-case skips**. Full opt-in run against the disposable SQL Server database: **464 passed, 0 failed, the same 4 skips**. Formatting verification passed.
- A separate **Coverlet console 10.1.0** measurement instrumented copied test outputs, not repository binaries, and ran the unit/live-SQL suites with the compiled xUnit runner. Merged reports measured **Core: 81.16% line / 80.51% branch; Tools: 86.68% line / 67.73% branch** (the two-library total was 85.37% line / 73.69% branch). These are not CLI/JavaScript/whole-repository percentages. The direct xUnit executable uses `-trait- Category=Integration` for unit-only collection, not the `dotnet test` platform's `--filter-not-trait`; full collection enables the disposable SQL environment and merges Core's JSON with Tools via `--merge-with`.
- The actual CLI validated its SQL connection. The official Inspector stdio smoke found all **9 tools**, called `list_databases`, and verified idempotency hints. Real MCP calls additionally proved null-row budgeting, escaped-key accounting, disabled caps, structured oversized-plan refusal, and session usability after refusal.
- All six runtime profiles passed locked restore and publish; NuGet pack succeeded. The published trimmed linux-x64 executable actually queried SQL Server. Other operating systems were cross-published, not executed.
- The actual local npm main/native archives installed offline with scripts disabled; matching versions resolved from local `file:` archives, and the installed shim ran the built binary. A separate main-only offline install with downloads disabled exited 1 with actionable GitHub/NuGet alternatives.
- Docker locked restore/build passed with the canonical hashes. A live container query then exposed a real `Globalization Invariant Mode is not supported` failure missed by `--version`. The runtime base now uses the digest-pinned **.NET 10 Alpine extra** image, which includes ICU and does not force invariant mode. Container connection validation and actual MCP database calls, XML refusal, and follow-up query succeeded after the change. CI now validates a real container database connection against a digest-pinned SQL Server 2022 test service.
- The release-policy/version-consistency suites, README snippet/badge linter, npm smoke suite, and behavioral archive/redirect/cache security regressions passed. Obsolete source-text and export-only assertions are removed; redirect checks exercise rejection before a second request and the depth boundary.
- Actual C# language-server diagnostics reported zero errors for the changed solution sources and the separately loaded nonshipping fuzz project. Hosted PR CI, CodeQL and fuzz executions passed and their analysis records/artifacts were inspected above. A fresh merged-main Scorecard result still governs alert disposition; passing scans are not a security guarantee.

## Required verification and external actions

1. A human must review and submit an accurate best-practices application and obtain the external badge; [issue #140](https://github.com/codegiveness/mssql-mcp/issues/140) tracks that real prerequisite.
2. A separate qualified maintainer/reviewer must participate; genuine approvals and maintenance history must accumulate. ADR-0033's zero-review tradeoff remains in force until that prerequisite changes.
3. Exercise the actual fuzz runner, inspect managed coverage and campaign artifacts, and remediate any genuine findings. Deterministic tests cannot replace the coverage-guided campaign.
4. Merge through the repository's normal rules, run CodeQL and Scorecard on the new `main`, review diagnostics, and settle alerts from observed evidence. Do not publish 1.x or dismiss findings solely to make a score green.
