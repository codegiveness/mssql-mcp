# Roslyn analyzer strategy: hybrid suppression via NoWarn + targeted fixes

Enable `<AnalysisLevel>latest-recommended</AnalysisLevel>` repo-wide via `Directory.Build.props`, suppress 6 high-volume rules via `NoWarn`, and fix the remaining 2 in code. Supersedes the analyzer portion of ADR-0014 (which left `<AnalysisLevel>` unset per-project); ADR-0014's body otherwise stands.

## Context

A repo-wide audit (`dotnet build -p:AnalysisLevel=latest-recommended -p:TreatWarningsAsErrors=true`) surfaced 876 warnings across 8 rules: CA1707 (590 — underscores in xUnit test names and namespaces), CA1848 (110 — `LoggerMessage` source generator), CA1861 (68 — constant arrays passed to test args), CA1873 (38 — expensive logging arg evaluation), CA1305 (36 — `IFormatProvider`), CA1863 (26 — cache `CompositeFormat`), CA1859 (6 — prefer concrete types in test helpers), CA1310 (2 — culture string comparisons). The baseline ships clean with no `<AnalysisLevel>` set; adopting `latest-recommended` requires addressing all 876 warnings to preserve `TreatWarningsAsErrors`.

## Decision

Adopt a **hybrid** strategy: (1) enable `<AnalysisLevel>latest-recommended</AnalysisLevel>` repo-wide via `Directory.Build.props`, (2) suppress the 6 high-volume / low-value-to-fix rules via `<NoWarn>$(NoWarn);CA1707;CA1848;CA1873;CA1863;CA1861;CA1859</NoWarn>` with inline XML comments documenting each suppression's rationale and hit count, and (3) fix CA1305 and CA1310 in code (issue #12) because the fixes are small and correctness-relevant (culture-aware string formatting/comparison). CA1707 is a permanent suppression (idiomatic xUnit `test_method_naming` and the `mssql_mcp` namespace matching the package name); the other 5 are deferred to post-1.0 as a tracked v2 ticket.

### Considered alternatives

- **Fix-all in 1.0**: 876 changes across the codebase — too much churn for the stability commitment, risks merge conflicts across the P0 sweep. Rejected.
- **Suppress-with-tracking-list** (per-call-site `#pragma` or `[SuppressMessage]`): preserves per-call-site visibility but adds ~876 attributes/pragmas and a maintenance burden. Rejected — `NoWarn` is MSBuild-native, centralizes the rationale in `Directory.Build.props` with inline comments, and the 6 rules are uniformly deferrable.
- **Hybrid** (chosen): lowest-churn path to a clean `latest-recommended` build with `TreatWarningsAsErrors`, while preserving the 2 correctness-relevant rules as real fixes.

## Consequences

- `dotnet build -c Release` is clean (0 warnings, 0 errors) with `<AnalysisLevel>latest-recommended</AnalysisLevel>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` repo-wide — both now centralized in `Directory.Build.props`. Individual csprojs retain their existing `<TreatWarningsAsErrors>` (csproj precedence; no conflict).
- The 6 suppressed rules are deferred to post-1.0 and tracked in a v2 ticket. Re-enabling each is a one-line `Directory.Build.props` edit once the call sites are fixed.
- CA1305 and CA1310 are fixed in code (issue #12) — those fixes are permanent.
- StyleCop and Roslynator analyzer packs are deferred to post-1.0 (P1, not P0) — this ADR scopes only the built-in .NET analyzers.
- `<PublishRepositoryUrl>` and `<EmbedUntrackedSources>` (also in `Directory.Build.props`) feed the Source Link determinism pipeline (#17, #18).

## Status

Accepted (2026-07-21). New (supersedes nothing; refines the analyzer strategy implied by ADR-0014 §"Workflows").
