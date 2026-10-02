# Third-Party Notices

This file lists third-party software distributed with mssql-mcp, along with
their license terms. Source links are provided for verification.

## Microsoft.Data.SqlClient

- License: MIT
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/SqlClient
- Purpose: ADO.NET data provider for Microsoft SQL Server. Used in all build
  configurations (NuGet package and self-contained binaries).

## Microsoft.Data.SqlClient.Extensions.Azure and authentication libraries

- License: MIT for `Microsoft.Data.SqlClient.Extensions.Azure`, its
  `Extensions.Abstractions` / `Internal.Logging` companions, Azure.Core,
  Azure.Identity, Microsoft.Identity.Client, Microsoft.Identity.Client.Broker,
  and Microsoft.Identity.Client.Extensions.Msal.
- Copyright: Microsoft Corporation / .NET Foundation and Contributors.
- Sources: https://github.com/dotnet/SqlClient,
  https://github.com/Azure/azure-sdk-for-net,
  https://github.com/AzureAD/microsoft-authentication-library-for-dotnet.
- Purpose: Preserve driver-provided Microsoft Entra authentication after the
  SqlClient 7 package split.

### Microsoft.Identity.Client.NativeInterop — redistribution hold

- License: Microsoft Software License Terms, **not MIT**.
- Package: https://www.nuget.org/packages/Microsoft.Identity.Client.NativeInterop/0.20.6.
- The official package's `LICENSE`, section 3(e), prohibits sharing/publishing/
  distributing the software. It contains native broker assets for Windows,
  Linux x64, and macOS; it is a transitive authentication dependency.
- Local installation and verification are distinct from permission to redistribute.
  The owner selected retention of Entra support with public redistribution blocked
  pending licensing clearance. `scripts/check-redistribution.js` enforces the hold
  before artifact production/publication in the Release workflow and withholds
  distributable NuGet/npm uploads in CI without disabling verification reports.
- Do not publish the affected binaries, container, or NuGet/npm artifacts without
  confirmed redistribution rights or a reviewed dependency change that removes the
  restricted software while preserving the supported authentication contract.
  The MIT license of this repository does not grant rights to this dependency.

## Microsoft.Data.SqlClient.SNI.runtime

- License: Microsoft "Distributable Code" license
- Copyright: Copyright (c) Microsoft Corporation
- Source: https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime
- Purpose: Native SNI (Session Network Interface) for Windows. Transitive
  dependency of Microsoft.Data.SqlClient on Windows.
- Note: The Microsoft "Distributable Code" license contains anti-copyleft
  clauses (§3.a.iii) that conservatively block redistribution inside a
  self-contained binary under an MIT-licensed project. For this reason,
  Windows builds of mssql-mcp are **framework-dependent** (the SNI native
  component is resolved via NuGet restore on the user's machine, not
  redistributed inside our binary). Linux and macOS builds use the managed
  SNI implementation, which is MIT-licensed and freely redistributable.
  See ADR-0002 for the full rationale.

## Microsoft.SqlServer.TransactSql.ScriptDom

- License: MIT
- Copyright: Copyright (c) Microsoft Corporation
- Source: https://github.com/microsoft/SqlScriptDOM
- Purpose: T-SQL parser and AST generator. Used by the Guard (ADR-0006) to
  validate SQL statements in Restricted mode.

## ModelContextProtocol (C# SDK)

- License: Apache-2.0
- Copyright: Copyright (c) ModelContextProtocol contributors
- Source: https://github.com/modelcontextprotocol/csharp-sdk
- Purpose: Official MCP SDK for .NET. Provides stdio transport, tool
  registration, and protocol handling (ADR-0008).

## .NET 10 Runtime (self-contained builds only)

- License: MIT
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/runtime
- Purpose: .NET runtime bundled into self-contained builds (Linux x64/arm64,
  macOS x64/arm64). Windows builds are framework-dependent and do not
  redistribute the runtime.
