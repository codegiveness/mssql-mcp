# Distribution: dotnet tool (primary) + npm wrapper for Linux/macOS self-contained, Windows framework-dependent

Publish the server two ways. (1) **Primary**: a `dotnet tool` on NuGet — clean, idiomatic .NET, mirrors the standard .NET tool distribution model, and sidesteps the SNI redistribution problem because NuGet restore fetches `Microsoft.Data.SqlClient.SNI` on the user's machine (we are not the distributor). (2) **Secondary**: an npm package that wraps the self-contained .NET binary using the Node shim + per-platform binary pattern (Node shim in `npm/bin/`, binary delivered via per-platform `optionalDependencies`). For Linux/macOS the self-contained binary uses managed SNI (MIT-clean). For Windows, the self-contained binary would bundle `Microsoft.Data.SqlClient.SNI` under the Microsoft 'Distributable Code' license — whose anti-copyleft clause (§3.a.iii) conservatively blocks redistribution under our MIT license. So Windows npm installs fall back to framework-dependent execution (user has .NET 10 runtime) or the `dotnet tool` path.

**Considered Options**:
- Self-contained binary for all platforms including Windows — rejected: SNI license risk under MIT.
- Framework-dependent everywhere — rejected: forces every Linux/macOS user to install .NET 10, hurting the "just works via npx" UX.
- Drop Windows support entirely — rejected: Windows is the dominant SQL Server dev platform.

**Consequences**:
- Linux/macOS users get `npx mssql-mcp` that just works. Windows users get a working `npx` experience only if .NET 10 runtime is present, otherwise must `dotnet tool install`. Document this in README.
- Release pipeline builds 4 self-contained RIDs (linux-x64/arm64, osx-x64/arm64) + publishes a NuGet tool package. Windows builds are framework-dependent.
- `THIRD-PARTY-NOTICES` must enumerate Microsoft.Data.SqlClient.SNI's Distributable Code terms even though we don't bundle it, because the dotnet tool path restores it.
