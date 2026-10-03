# Security and quality follow-up

This is a current disposition and verification checklist, not a rewrite of the historical security audits. It does not authorize a 1.x release. A configured control is not a successful execution, and a Scorecard score is not a vulnerability assessment.

## C# repository-tooling cutover

Current maintenance commands use `dotnet run --project tools/MssqlMcp.RepoTool -- <command>`.
The former repository-owned JavaScript, Python, and Bash scripts have been removed; historical
command transcripts and dated findings below describe the earlier implementation, not current
entrypoints. [ADR-0012](adr/0012-project-structure.md#repository-tooling-cutover) records the cutover.
The npm launcher and its consumer tests, official Inspector, upstream npm publisher, security
auditors, and native fuzz engines are retained. Security pins, redaction, advisory freshness,
zero-major publication policy, and required runtime verification remain in force.

Local cutover verification exercised the actual CLI rejection/repair paths, 171 repository-tool
regressions, and the live solution suite (650 passed, four existing ScriptDom-unreachable skips,
zero failed). The official Inspector stdio handshake found nine tools, called `list_databases`,
and checked all idempotency annotations. A native fuzz smoke campaign completed 30,148 runs
and produced the separately required intentional-crash probe; its campaign findings directory
was empty. The content-pinned scanner installers and source/SRI-locked npm 12.2.0 installation
ran successfully, and that installed publisher passed its version command and packaging dry run.
Fresh Trivy assessments covered the actual Docker image, generated solution SBOM, Inspector/c8
lock, and npm publisher lock with zero HIGH/CRITICAL findings. Detector/redaction verification
and actionlint/zizmor/ShellCheck workflow auditing also passed. These are local runtime proofs,
not evidence of registry publication or a new hosted CodeQL run.

The npm installer smoke exposed trailing-separator path identity differences: canonical contained
paths now remove trailing directory separators before parent creation or archive-member
deduplication. The directory-alias regression failed before the fix and passed afterward.
An initial live database-health test failure did not recur in the direct-tool diagnostic or
the full suite rerun; its assertion now includes the sanitized tool response for diagnosis.
No server behavior or distributed package version changed.

Full committed-history Gitleaks scanning completed with zero findings. The first local attempt
was stopped by an outer verification-harness deadline; using the scanner's existing ten-minute
allowance completed the full scan. No scanner policy or configured timeout was weakened.
GitHub branch protection was read back with 14 required, Actions-bound checks and administrator
enforcement: only the obsolete Python analysis requirement was retired, while the native
CodeQL HIGH/CRITICAL gate and its no-bypass policy remain active.

Hosted scanning of the first cutover revision flagged only the migrated detector generator's
static `"Synthetic"` prefix. The existing exception was ported to the exact C# module path,
retaining the Python path for committed history and the exact prefix-only secret match.
The detector smoke then detected all 12 positive fixtures, including a generated credential
at that same C# module path, while accepting the static prefix in the negative fixture.
No test-directory exception, real-credential exception, or history rewrite was introduced.

## .NET modernization verification

Official [.NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
identifies **SDK 10.0.401 / runtime 10.0.12** as the current stable .NET 10
release. .NET 11 is prerelease and was not adopted. The repository already targeted
`net10.0`; modernization upgrades the SDK pin, fixes **C# 14.0** instead of
`latest`, and aligns setup-dotnet and checksum/digest-pinned Docker assets.
All direct dependency/tool versions were checked against their official NuGet,
npm, Microsoft registry, or maintainer release metadata.

The retained stable application baseline includes **SqlClient 7.1.1**,
**Microsoft.Extensions 10.0.12**, **ScriptDom 180.117.0**, and **MCP SDK 2.2.0**.
SqlClient 7 separated Entra support into its
[Azure extension](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/azure-active-directory-authentication?view=sql-server-ver17).
After the modernization, the owner authorized removing that integration instead
of seeking clearance for its restricted native broker. SqlClient remains 7.1.1;
SQL password and Windows Integrated remain, but Entra authentication is unsupported.
All **24** portable, test, fuzz, and six-profile deployment locks were regenerated
against official NuGet content in a clean cache; locked restore and content-hash
validation remain enabled.

Measured defects and bounded findings:

- **Logging ownership:** before the fix, 25 create/log/dispose cycles retained
  25 file handles and added 25 console threads. Factory registrations give DI
  ownership of both providers. The same 25-cycle, 2,500-call workload afterward
  retained **zero file handles**, with **zero thread delta**; an exclusive-open
  regression verifies that a disposed host releases the sink for the next host.
- **SQL resources/cancellation:** connections, commands, and readers use async
  disposal where supported. SHOWPLAN OFF cleanup has an independent five-second
  cancellation budget even with query timeout zero. A confirmed schema-lock
  workload exposed cancellation as a SqlException during metadata reads; it now
  propagates request cancellation. The live regression and an actual MCP
  cancellation notification both recovered a one-connection pool while the
  competing lock remained held, and subsequent SQL returned **42**.
- **Row allocation:** three 100,000-row live-reader comparisons allocated about
  **42.40 MB** with per-row scratch arrays versus **37.60 MB** with a reused
  result-set array: approximately **11.3% less allocated memory** in this
  coercion workload. Row contents were checked throughout. This is not a server
  throughput or retained-memory claim.
- **Precision/work avoidance:** a 38-digit SQL decimal previously produced an
  INTERNAL overflow response; it now returns the complete decimal string.
  Unlimited summary-plan collection skips unnecessary UTF-8 budget accounting.
  An isolated comparison of the original and changed writers over 30 one-MiB
  ASCII plans measured **198–245 ms versus 49–63 ms** after warmup, with roughly
  unchanged **126.6 MB** allocations. This isolates character-accounting work,
  not SQL execution or end-to-end server performance.
  Thirty actual 2,000-row MCP queries completed before and after with clean EOF
  shutdown; **1.051 s versus 1.139 s** does not demonstrate a throughput gain.

Final checks: **452 unit successes / 479 full live-SQL successes**, zero failures,
and four existing unreachable-case skips. The solution build had zero warnings
or errors; changed-file C# LSP diagnostics had zero errors, and formatting was
clean. CLI help, unknown-argument exit/error, SQL validation, npm smoke, README
snippets/badges, version/release guards, redistribution-gate regressions,
checksum-verified actionlint installation, and workflow auditing passed.
The official Inspector stdio smoke passed all three checks: **nine tools**,
**one user database**, and idempotency annotations.
Verification loaded local `.env` without exposing or changing it, then selected
the existing isolated SQL Server user database instead of the configured endpoint.
A clean official NuGet cache avoided previously cached distro-repackaged linker
content; no restore, content-hash, or test gate was disabled.
Current-state acceptance also passed locked restore and publication for all six
deployment profiles, local NuGet tool packing, and the .NET 10/C# 14 fuzz-target
build. The newly published linux-x64 executable exercised all nine tools and
exited zero on EOF; non-Linux platform execution remains unverified.

Actual MCP sessions exercised all nine tools, DDL/DML/read round trips,
38-digit decimal output, metadata/discovery, XML and summary plans, index/query
and health diagnostics, cancellation, and EOF shutdown. Restricted-mode checks
verified write rejection, a 256-byte result budget and truncation notice,
oversized raw-plan refusal, uncapped summary collection, and session recovery.
The updated Alpine Docker image built, validated a live SQL connection, and
exercised all nine tools over real stdio, including the decimal and plan paths;
its EOF shutdown exited zero. Smoke-created tables were removed.
The Docker build emits two nonfatal SourceLink/repository-metadata warnings
because the build context excludes Git metadata; it does not produce usable
SourceLink information for the container executable.
The earlier provider-discovery smoke proved Entra integration before its removal;
it was not an authenticated Azure login and is not a claim of current support.

**The owner authorized removal of the restricted authentication graph.**
The Azure extension, reflection trimming root, and Azure.Identity/MSAL/native
broker dependencies are removed from portable and deployment inventories.
`scripts/check-redistribution.js` remains a fail-closed release/CI gate against
reintroduction; it now admits the broker-free application graph.
Actual output inspection also disproved the former Windows SNI workaround:
framework-dependent builds and portable tool packages copy native dependencies.
Shared build configuration excludes all native SNI assets and selects SqlClient's
documented managed-networking switch for Windows. The restored SNI package remains
version-aligned at 7.1.0 but none of its assets may be published.
The existing distribution architecture remains. These exclusions resolve the
identified restrictions, not general license compliance.

The removal cutover repeated the **452 unit / 479 live-SQL successes** and all
three mandatory Inspector checks, with zero failures and the same four skips.
All **24** inventories exclude the Azure/MSAL/native broker graph.
Locked restore and clean publication passed for all six exact deployment profiles,
including Windows' existing framework-dependent single-file flags.
Actual publication directories and the NuGet tool ZIP exclude native SNI and the
restricted authentication assemblies; the packaged runtime configuration enables
managed Windows networking. Provider discovery found no Entra providers for all
driver `ActiveDirectory` methods. The final Alpine image validated SQL and
exercised all nine tools, DDL/DML/read, decimal(38), metadata, XML/summary plans,
and diagnostics, then exited zero on EOF. Temporary database tables were removed.
The actual newly packed NuGet tool was installed from an isolated local feed and
exercised the same nine-tool workload with clean EOF shutdown. The published
linux-x64 executable passed that workload too.

Application logic remains .NET. The official Inspector, npm distribution,
Node-based release tooling, shell orchestration, and Python security/reproducible
packaging utilities retain independent capabilities; replacing them merely to
claim “100% .NET” would not improve the runtime. Inspector JSON processing now
uses its already-required Node runtime rather than an additional Python step.
Windows/macOS/ARM64 execution, exhaustive leak profiling, production load, and a
complete legal audit remain unverified. Entra authentication is intentionally unsupported.

## Baseline evidence and twelve active findings

Repository API inspection reported **12 open code-scanning alerts**, all produced by **OpenSSF Scorecard v5.5.0 on `main` at `d625a6b`**. It also reported zero open Dependabot vulnerability alerts, zero open secret-scanning alerts, and secret scanning, push protection, and dependency security updates enabled. Those are point-in-time observations. They do not establish that this working branch is free of vulnerabilities.

The alert numbers below identify the existing GitHub findings, not hypothetical new issues. Original line numbers belong to the scanned revision and will move with edits.

| Group | Alerts and original evidence | Disposition and required proof |
|---|---|---|
| Container dependencies | **#3** SDK image and **#4** runtime-deps image were tag-only Dockerfile references | Docker images now use registry digests, retaining .NET 10. Verify a real Docker build and executable smoke run, then rerun Scorecard against the merged revision. A digest does not establish image vulnerability status. |
| NuGet restore graphs | **#5** Dockerfile restore/publish, **#6** CI build (line 25), **#15** release publish (line 74) | Central versions plus generated lock files pin the actual direct/transitive resolved graph and content hashes. Bare central versions are minimum ranges, not exact pins by themselves. Shared props enable locks; explicit runtime restores select separate profile lock paths. CI/release/Docker use locked restore followed by no-restore build/publish/pack. Generate and commit every graph and exercise the workflow/Docker SDKs. Fresh merged-main Scorecard marked these original findings fixed; that is not a package-vulnerability assessment. |
| Local npm package smoke installs | **#7** CI line 152 and **#8** CI line 144 flag `npm install ... ./*.tgz` | The arguments are local archive paths, not an unversioned registry tool. CI builds the native binary, stages it in the platform package, packs both manifests, and copies those packs into isolated install directories. Main manifest optional dependencies specify exact versions. An inspected stale platform manifest (`0.5.2` versus main `0.5.5`) meant the old source alone was insufficient proof of registry independence. All five platform stamps now belong to the authoritative sync/version-consistency scripts, rather than a CI-only workaround. CI installs with `--offline --ignore-scripts` and invokes the installed local executable directly, avoiding `npx` registry fallback. Successful offline installs, inspection of `file:` archive resolutions, and execution of the staged binary are required false-positive evidence. Do not dismiss either alert merely from its command spelling. |
| Static analysis | **#10** SAST reported 0 of 27 recent commits checked | The first hosted C#/JavaScript deployment used SHA-pinned init/analyze v4.38.2 with `security-extended` queries. It runs on pushes to `main`, PRs and weekly; the current working rollout expands to `security-and-quality` as documented below. Require successful extraction, diagnostic review and uploaded analyses for both languages. Historical unchecked commits cannot be retroactively made scanned by adding a workflow. |
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

## Security-control rollout

[PR #150](https://github.com/codegiveness/mssql-mcp/pull/150) merged through normal protections at `de7648f`, after all 15 required checks passed. The bundled npm blocker is resolved through unmodified, SHA-verified upstream CLI source with an independently SRI-locked production graph. Both retained locks passed local and hosted fresh HIGH/CRITICAL scans. Merged-main execution and retained-corpus reuse are recorded below. `npm publish --dry-run` proves packaging, not publication or OIDC authentication.

- **CodeQL and merge enforcement:** the workflow selects `security-and-quality` for C#, JavaScript/TypeScript and Python. The active [native ruleset 24351321](https://github.com/codegiveness/mssql-mcp/rules/24351321) targets `main`, has no bypass actors and blocks CodeQL `high_or_higher` alerts. After every final PR workflow job passed, branch protection was expanded and read back with **15 strict required checks bound to GitHub Actions**: existing `build`, `validate`, C#/JavaScript analyses and `sharpfuzz`, plus Python analysis, `security-regressions`, `secrets`, `workflow-audit`, `artifact-audit`, `docker`, `npm-pack`, `smoke`, version `check` and `release-policy`. No existing protection was removed or bypassed.
- **PR security proofs:** an isolated SQL Server service backs the existing SQL/Guard integration suites. The actual local server, Docker connection validation, CLI acceptance checks and locked official Inspector **2.9.0** smoke passed; Inspector returned **9 tools**, **1 database**, and the expected idempotency annotations. Memory-only secret storage and the v2 JSON response envelope replace the obsolete v1 client contract.
- **Content pins and auditors:** Inspector 2.9.0 and c8 use `.config/npm-tools/package-lock.json`. `scripts/install-npm-cli.py` SHA-256-verifies official npm 12.2.0 source, checks that `.config/npm-cli/package.json` matches upstream version/dependencies/engines, and installs its SRI-locked production graph with lifecycle scripts disabled. The source's old bundled tree and unused development fixtures/locks are discarded, not executed. Retained CLI source files are unmodified; this is not a byte-identical official npm release tarball or a maintained code fork. Release invokes that CLI directly. SHA-256-pinned Gitleaks, Trivy, zizmor, actionlint and ShellCheck installs were exercised.
- **Secrets:** synthetic detector verification found all **11** expected redacted positives and no negative-fixture findings; deleted-history, scoped-exception and ignored-`.env` checks passed. Full committed history returned zero findings after exact historical synthetic-notation exceptions. This does not scan uncommitted working-tree changes. GitHub non-provider scanning and validity checks still read back as disabled after the attempted settings update; no activation or entitlement is claimed.
- **Artifact scans:** the actual Alpine image and solution CycloneDX SBOM passed fresh HIGH/CRITICAL scans. After source-based npm dependency installation, the Inspector/c8 lock assessed **193 packages** and the publisher runtime lock **145 packages**, both with **zero HIGH/CRITICAL findings**. No advisory exceptions or severity filters were weakened.
- **Coverage:** the exercised Coverlet.MTP live-SQL collection measured production Core + Tools at **83.6% line / 79.8% branch**. Native V8 security-regression collection measured the npm shim at **40.6% line / 85.9% branch**. Reports retain raw data, Cobertura and HTML source views. These are scoped measurements, not whole-repository coverage or proof of security.
- **Fuzzing:** a **600-second** campaign completed **2,279,805 executions in 601 seconds**, reached **38,994 features**, and ended with **3,610 corpus entries**. The independent crash-detection probe emitted the expected artifact; the product campaign emitted no findings. A second fresh runner imported the retained corpus and completed **3,611 executions** with **38,992 features**. Hosted cache upload/restore remains unverified.

| Original blocking advisory | Original bundled package | Fixed compatible version |
|---|---|---|
| [CVE-2026-102276](https://avd.aquasec.com/nvd/cve-2026-102276) | `brace-expansion` 5.0.9 | 5.0.10 or newer |
| [CVE-2026-102278](https://avd.aquasec.com/nvd/cve-2026-102278) | `brace-expansion` 5.0.9 | 5.0.11 or newer |
| [CVE-2026-19534](https://avd.aquasec.com/nvd/cve-2026-19534) | `undici` 6.28.0 | 6.28.1 or newer |

The maintained npm 11.21.0 tarball and ordinary source/bundle installs retained the same advisories. The final installer instead follows upstream's source file inventory and installs the unchanged declared runtime dependencies through normal `npm ci`: **brace-expansion 5.0.12** and **undici 8.11.2** satisfy their current consumers' ranges. Both active locks passed with freshly downloaded advisory databases; the final CLI reported **12.2.0** and completed a real publication dry-run. No overrides, vendor-file patches, excluded advisories or global npm replacement are retained. One local scan failed while downloading its database because of native temporary-storage quota; scratch was relocated to native disk and both scans then passed. The wrapper now deletes earlier report/metadata files before scanning, so failed reruns cannot present stale findings as fresh.

The [canonical assessment proposals](../.bestpractices.json) support the service's repository automation, with personal attestations and unverified TLS guarantees left unknown. The maintainer subsequently supplied [project 15156](https://www.bestpractices.dev/en/projects/15156/passing); its public JSON and live badge report **19%**, `in_progress`, and no achieved-passing timestamp. Its homepage URL is empty and most engineering answers remain unknown. The authenticated relay still reports its extension disconnected. Saving reviewed proposals and obtaining the real passing result ([#140](https://github.com/codegiveness/mssql-mcp/issues/140)), genuine independent reviews/history and resolving the explicit npm account suspension ([#148](https://github.com/codegiveness/mssql-mcp/issues/148)) remain prerequisites. No award, account approval, 1.x qualification or registry publication is fabricated.


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

The C# repository tool's `run-fuzz` command performs locked restore and publish, installs the exact instrumentation tool version 2.3.0, and instruments Core's Guard types plus ScriptDom. It downloads the native libFuzzer bridge from commit `bd39d4e88d715ab460a929943645be2a186cde52`, verifies SHA256 `90f019e2e9ad3a0b93c7ecc2c5afb2fbfc8b5aab6aac51c7e0d349ec79354f36`, and compiles it with `clang++ -fsanitize=fuzzer`. This follows the supported native bridge rather than substituting hand-written random mutation or source markers.

The callback parses input directly before invoking `SqlGuard.ValidateStrict` because Guard catches parser exceptions; unexpected parser exceptions must remain visible to libFuzzer. Ordinary ScriptDom parse-error results are valid outcomes. If Guard accepts a batch, a separate visitor asserts that all statements are SELECT and none has an INTO target. The harness never opens a SQL connection. Four seed files cover valid SELECT, CTE/Unicode/quoted values, nested writes/batch separators, and malformed comments/strings; `fuzz/sql.dict` guides mutation.

The runner first uses `MSSQL_FUZZ_CRASH_PROBE=1` for a separate deliberate managed exception and requires both a failed engine run and its crash artifact/error message. That artifact validates the engine, **not a product defect**. The actual campaign explicitly removes this variable, fixes the mutation seed to 1, limits input to 4096 bytes, uses a five-second per-input timeout, and runs for 60 seconds in ordinary CI or 600 seconds on the weekly schedule (local duration accepts 1–1200 seconds). The C# process runner bounds unresponsive engines, preserves partial logs, and returns timeout exit 124. Real crashes/timeouts remain campaign failures with artifacts; these limits are not a total managed-process memory guarantee.

Generate the harness's real portable lock separately after pin changes because the project is deliberately outside the main solution:

```sh
dotnet restore fuzz/mssql-mcp.Fuzz/mssql-mcp.Fuzz.csproj --force-evaluate

# Requires .NET 10, clang++/compiler-rt libFuzzer, and curl.
# Use a new directory; the runner refuses to overwrite existing campaign output.
dotnet run --project tools/MssqlMcp.RepoTool -- run-fuzz /tmp/mssql-mcp-fuzz-proof 60
```

The output directory retains `instrumentation.log`, `crash-probe.log`, `campaign.log`, the mutated `corpus`, separately labeled `probe` artifacts, and real `findings`. Review managed coverage/features/executions and crash status; a successful exit alone is insufficient proof. `.github/workflows/fuzz.yml` uploads these artifacts even on failure and grants only `contents: read` without elevated PR permissions. Retained corpus imports remain bounded, top-level data only; linked roots/files and nonseekable samples are not imported.

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

### Additional closure proof

The read-only residual review identified three targeted controls, all reproduced before remediation. Restricted Guard accepted `SELECT NEXT VALUE FOR`; a real configured log path with a linked parent created the destination file; and the actual CLI accepted `--access-mode 2 --validate` and validated SQL. After remediation:

- Actual MCP stdio `execute_sql` returned `GUARD_REJECTION` / `next_value_for` with line/column information. Ordinary queries returned **42**, then **43** after refusal in the same session. Unrestricted behavior remains covered separately. Sequence impact requires the login's UPDATE grant; this is not SQL privilege escalation.
- The actual file logger rejected the linked parent before creating a destination. Final links, linked/dangling ancestors and all archive positions have deterministic regression coverage; ordinary append/rotation passed. Dangling final links already rejected in the before-run and are not falsely claimed as a reproduced bypass. Private server-owned directories remain necessary against concurrent path replacement.
- Startup rejects numeric/combined access modes. The actual formerly accepted CLI invocation exits **1** before connection validation. Only explicit Unrestricted mode enables the SQL write route, including programmatic options; ordinary named modes preserve case/whitespace handling.
- Integrated unit suite: **461 passed**; full opt-in disposable SQL suite: **486 passed**; both zero failures and four existing unreachable-case skips. Formatting passed. Actual C# LSP pull diagnostics returned zero errors for all changed solution C# sources and the separately loaded fuzz project; JavaScript/Python servers were unavailable, with their real runtime/syntax and hosted-analysis checks retained.
- The updated sequence-aware fuzz campaign executed **36,985 inputs in 11 seconds**, with a successful independent managed-crash probe and no product finding. Earlier long-campaign evidence above predates this invariant update.
- Actual CLI validation/help/unknown-argument behavior and official Inspector 2.9.0 handshake passed. Final Docker image reported **0.5.6.0** and validated SQL, and its fresh HIGH/CRITICAL scan assessed **21 OS packages** with no finding. Final solution SBOM scan assessed **85 packages** with no finding. Docker still emits two SourceLink metadata warnings because its context omits `.git`; normal solution build has zero warnings/errors.
- A real failed advisory download with an unreachable proxy exited **1**, removed earlier report/metadata files, and emitted no stale package counts. Working fresh scans use native disk scratch after a local temporary-storage quota failure; scan gates and advisory policies are unchanged.

### Hosted rollout findings

The initial [PR #150 CodeQL run](https://github.com/codegiveness/mssql-mcp/actions/runs/36979559544) successfully uploaded `security-and-quality` analyses: **164 C#**, **201 JavaScript/TypeScript** and **172 Python** rules, each with empty API error/warning fields. This does not establish complete security coverage.

- **#25–#27 (Python HIGH cleartext storage):** full downloaded SARIF traces source only the generated synthetic detector input at `verify_detectors` line 182 or the literal `Password` key construction at line 184—not user credentials. Sinks intentionally write disposable detector repositories and an ignored `.env`; none authenticates to a live service. Actual detector verification still detected all **11 positives**, zero negatives, deleted history, scoped exceptions and complete redaction. These specific flows are classified `used in tests`; real credential storage is not excused or excluded.
- **#28–#37 (C# path-combine notes):** full SARIF contains a generic API recommendation, not an absolute-argument trace. Every later operand in the ten flagged test calls is a fixed relative literal (`destination`, `nested`, `linked-parent`, `app.log` or `destination.log`). None can replace the first path. Live linked-path/rotation cases passed. These specific notes are classified false positive; the code/tests were not changed merely to silence them.
- **Initial secret job:** the SQL-password regex matched the exact quoted `"Synthetic"` prefix in the generator's Python assignment, including Git's PR merge patch. An exception is restricted to that exact non-credential literal, `scripts/security-scan.py`, and the SQL-password rule. Random/generated credentials and provider rules remain enabled. Detector proof passed again; a native mirror of the full committed history returned zero findings. The earlier mounted-filesystem scan exceeded its outer 120-second command deadline; it was not reported as a successful scan.
- **Hosted live proof:** [CI security-regressions](https://github.com/codegiveness/mssql-mcp/actions/runs/36979559538) passed **240 Core + 246 Tools** cases, zero failures and the four existing skips. Inspector performed the real stdio handshake: **9 tools**, **1 database**, correct annotations, **3 passed / 0 failed**. The published source coverage artifact measured Core + Tools **84.2% line / 80.1% branch**, and shim **85.9% branch**. Scope is production assemblies and targeted shim regressions, not every repository file.
- **Final PR gate:** the [final Security run](https://github.com/codegiveness/mssql-mcp/actions/runs/36981038515) passed secrets, workflow and artifact audits. The [final CodeQL run](https://github.com/codegiveness/mssql-mcp/actions/runs/36981038509), [CI run](https://github.com/codegiveness/mssql-mcp/actions/runs/36981038538) and [Fuzz run](https://github.com/codegiveness/mssql-mcp/actions/runs/36981038532) passed, alongside version/release-policy and CLI validation. Individually classified findings remain visible as dismissed, not hidden by exclusions. The initial retained hosted fuzz artifact completed **376,982 executions in 61 seconds** and includes the independent intentional-crash artifact.

### Merged-main acceptance

- At `de7648f`, [CI](https://github.com/codegiveness/mssql-mcp/actions/runs/36981969626), [CodeQL](https://github.com/codegiveness/mssql-mcp/actions/runs/36981969750), [Security](https://github.com/codegiveness/mssql-mcp/actions/runs/36981969752), [Fuzz](https://github.com/codegiveness/mssql-mcp/actions/runs/36981969754), version/release policy and Release Please completed successfully. CI exercised live SQL, the actual Inspector stdio handshake, offline npm consumer installation and the built Docker image. The Scorecard workflow publishes its results rather than a downloadable run artifact; absence of such an artifact is not a failed analysis.
- The second main-branch fuzz campaign actually restored attempt 1's cache, loaded **1,652 seed files**, completed **325,463 executions in 61 seconds**, passed the independent crash probe and saved its updated corpus. This exercises reuse, not merely cache configuration.
- The [published Scorecard report](https://api.securityscorecards.dev/projects/github.com/codegiveness/mssql-mcp) identifies `de7648f` and scores **6.3**, not a security guarantee. It recognizes the real in-progress badge (2), SAST configuration with **6/30** recent commits checked (7), and dependency pins (9). Its remaining npm-command warnings name the two verified offline local-tarball consumer tests; its fuzz/signature heuristics do not replace the actual campaign and attestation evidence.
- New **Branch-Protection #38** scores 4 because `main` requires zero approvers. API readback confirms admin enforcement, strict GitHub Actions requirements, code-owner/stale-review controls and disabled force pushes/deletions. The zero-approval policy remains ADR-0033's explicit tradeoff; #38 stays open alongside **#10/#12/#13/#14**. No genuine independent approvals or maintenance history were invented.
- The release-note assessment uncertainty is resolved by dated, read-back-verified security clarifications on **v0.4.2**, **v0.5.2** and **v0.5.6**, identifying their public findings without changing tags/artifacts or claiming that PR #150 shipped in v0.5.6. The canonical proposal now marks `release_notes_vulns` Met. The five remaining unknowns require primary-developer knowledge confirmation or TLS-policy/handshake evidence; external project answers remain unsaved.
- A later local smoke against the `.env`-selected connection failed at `list_databases` (Inspector exit 5). The actual three-check smoke then passed against the existing isolated SQL container, after loading `.env` and explicitly selecting that fixture. No default credential/connection or timeout was changed, and the default `.env` target is not claimed healthy.

### Full-main quality-finding triage

The expanded [CodeQL analysis of `902815b`](https://github.com/codegiveness/mssql-mcp/actions/runs/36983281332) surfaced **55 additional quality notes/warnings (#39–#93)** without security-severity ratings. Successful analysis was not treated as their resolution. Every source site was reviewed, including two independent read-only reviews of tool boundaries and filesystem fixtures.

| Findings | Source-backed disposition |
|---|---|
| **#39/#40** | Remove incidental-default/source-string/mock-echo assertions from `GetTopQueriesTests`, not re-pin them to another lookup implementation. Twelve obsolete cases are removed; meaningful limit transformations, truncation, empty JSON and error mapping remain. |
| **#41/#44/#45** | Replace the constant `null != null` fixture expression with the actual nullable rejection fields, preserving the established byte-compatible JSON contract. |
| **#42/#43** | Remove the two allocated parse-error lists that `parser.Parse(..., out errors)` immediately overwrote. Definite assignment is satisfied on the continuing path; both catches return before use. |
| **#46–#51** | False positives: the typed nulls supply anonymous-property types. An actual .NET 10 compiler smoke succeeds with them and fails with **CS0828** after removing them. The wire-compatibility acceptance contract in closed #47 is retained. |
| **#52–#62** | Intentional error boundaries, not silently successful exception handling. Classification failures produce no statements, which `ExecuteSql` rejects before execution even in Unrestricted mode. Tool handlers preserve request cancellation, timeout/SQL mapping and `isError=true` INTERNAL envelopes; they do not undo committed Unrestricted statements. The stale classifier raw-execution comment is corrected. |
| **#63–#69/#74–#79** | Cleanup catches are separate from primary assertions. Review nevertheless found the real shared-file deletion in the static traversal fixture (#68/#79): the test deleted a pre-existing `/tmp/poc-static.log` while reporting success. Reproduced in an isolated TMPDIR before the fix; after atomically creating an owned fixture root and deleting only it, the identical sentinel survives unchanged. The permanent rejection test additionally verifies an existing escaped target remains unchanged. No generic “test-only” exemption excuses the deletion defect. |
| **#70–#72** | Retain direct loops. Optional LINQ rewrites add iterator/delegate layers without removing traversal or destination storage; the row-mutation loop must preserve unaffected rows and carry its successful lookup value. No fabricated benchmark or blanket LINQ conversion. |
| **#73** | Remove the redundant explicit enum `ToString` before append; actual file output still contains `[Information]`. The isolated allocation comparison measured **2,400,000 bytes for each form over 100,000 iterations**; no allocation reduction is claimed. |
| **#80–#93** | Later operands are fixed relative literals or fixed prefixes plus Guid N values; the two `..` paths deliberately exercise rejection. None can replace the first argument with an absolute path. The shared-file cleanup operation is removed for its independently reproduced ownership defect, not dismissed merely because its `Path.Combine` operands are constants. |

Post-fix verification: **449 unit / 474 full isolated live-SQL successes**, zero failures and four existing unreachable-case skips; build and actual changed-file C# LSP diagnostics have zero errors, and formatting is clean. Actual stdio sessions in both modes returned **42**, rejected malformed SQL with `parse_error`, then returned **43** in the same session. Live DMV calls verified negative-limit clamping and all six orderings using the API's numeric-string counters; the smoke supplies the schema-required nullable arguments explicitly. The official Inspector's three checks, native CLI, npm smoke, README and version checks passed. Genuine policy/history/badge residuals remain open; fresh hosted analysis must establish fixed versus individually documented intentional/false-positive dispositions.

## Required verification and external actions

1. A human must review and submit an accurate best-practices application and obtain the external badge; [issue #140](https://github.com/codegiveness/mssql-mcp/issues/140) tracks that real prerequisite.
2. A separate qualified maintainer/reviewer must participate; genuine approvals and maintenance history must accumulate. ADR-0033's zero-review tradeoff remains in force until that prerequisite changes.
3. Exercise the actual fuzz runner, inspect managed coverage and campaign artifacts, and remediate any genuine findings. Deterministic tests cannot replace the coverage-guided campaign.
4. Merge through the repository's normal rules, run CodeQL and Scorecard on the new `main`, review diagnostics, and settle alerts from observed evidence. Do not publish 1.x or dismiss findings solely to make a score green.
