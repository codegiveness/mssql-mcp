# Distribution: dotnet tool (primary) + npm wrapper for Linux/macOS self-contained, Windows framework-dependent

## Context

The server is distributed to .NET developers through a NuGet tool and to the broader MCP ecosystem through npm/npx. Native dependencies must not introduce redistribution requirements the project is trying to avoid.

## Decision

Publish two ways: (1) a primary NuGet `dotnet tool`; (2) an npm wrapper with per-platform `optionalDependencies` carrying the .NET executable. Retain the existing four self-contained Linux/macOS builds and framework-dependent Windows build to avoid changing installation contracts during this cutover. The Windows runtime requirement is a deployment choice, not proof that native dependencies are absent.

## Entra dependency removal

The distribution architecture above remains unchanged. The restored SqlClient 7
Entra extension introduced `Microsoft.Identity.Client.NativeInterop`, whose
packaged license prohibits redistribution. The owner subsequently authorized
removing the affected integration instead of seeking licensing clearance.
The Azure authentication extension, trimming root, and transitive broker graph
are removed; Entra connection-string authentication is no longer supported.
Release and CI retain the fail-closed redistribution guard, which admits the
broker-free graph. See [Third-Party Notices](../../THIRD-PARTY-NOTICES.md).

## Native SNI asset exclusion

Actual publication exposed a flaw in the earlier rationale: framework-dependent
Windows and portable NuGet tool packages also bundle native SNI. Its Microsoft
license grants object-code redistribution subject to additional requirements.
Avoid those requirements by excluding every `Microsoft.Data.SqlClient.SNI.runtime`
asset in shared build configuration; keep its restored version aligned with the
driver rather than suppressing or hand-editing NuGet's dependency graph.

Use MIT-licensed managed SNI on all platforms. Shared runtime configuration enables
SqlClient's documented `Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows`
switch for the server, tests, and fuzz executable. SQL password and Windows
Integrated Authentication remain the supported authentication contract.
Verify clean publication directories and the actual NuGet tool archive: changing
only `SelfContained` is not a redistribution safeguard.


## Considered Options

- Change Windows to self-contained in this cutover — rejected: changes the existing deployment contract; managed SNI asset exclusion does not require that change.
- Framework-dependent everywhere — rejected: forces every Linux/macOS user to install .NET 10, hurting the "just works via npx" UX.
- Drop Windows support entirely — rejected: Windows is the dominant SQL Server dev platform.

## Consequences

- Linux/macOS users get `npx mssql-mcp` that just works. Windows users get a working `npx` experience only if .NET 10 runtime is present, otherwise must `dotnet tool install`. Document this in README.
- Release pipeline builds 4 self-contained RIDs (linux-x64/arm64, osx-x64/arm64) + publishes a NuGet tool package. Windows builds are framework-dependent.
- Third-party notices explain the excluded native SNI package and the managed-networking replacement. Windows execution remains a separate platform-verification requirement.
