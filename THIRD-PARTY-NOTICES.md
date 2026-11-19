# Third-Party Notices

This file lists third-party software distributed with mssql-mcp, along with
their license terms. Source links are provided for verification.

Verbatim license texts for the redistributed components are in the
[`LICENSES/`](./LICENSES/) directory, and this file, `LICENSE` and `LICENSES/`
ship in every distribution channel: the NuGet tool package, the self-contained
release archives (next to the binary), and the `@codegiveness/mssql-mcp` and
per-platform npm packages. See [Bundled license texts](#bundled-license-texts).

## Microsoft.Data.SqlClient

- License: MIT
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/SqlClient
- Purpose: ADO.NET data provider for Microsoft SQL Server. Used in all build
  configurations (NuGet package and self-contained binaries).

## SqlClient extension abstractions and internal logging

- License: MIT
- Copyright: Microsoft Corporation / .NET Foundation and Contributors.
- Source: https://github.com/dotnet/SqlClient.
- Purpose: `Microsoft.Data.SqlClient.Extensions.Abstractions` and
  `Microsoft.Data.SqlClient.Internal.Logging` remain driver dependencies.

## System.Security.Cryptography.Pkcs

- License: MIT
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/runtime
- Purpose: SqlClient transitive dependency, resolved to 9.0.21 (not affected by
  CVE-2026-84008); no direct pin is needed.

## Removed Entra authentication integration

`Microsoft.Data.SqlClient.Extensions.Azure` and its Azure.Identity / MSAL
authentication graph are no longer included. Their transitive
`Microsoft.Identity.Client.NativeInterop` 0.20.6 package has Microsoft Software
License Terms, not MIT; section 3(e) prohibits sharing/publishing/distributing
the software. Package: https://www.nuget.org/packages/Microsoft.Identity.Client.NativeInterop/0.20.6.

The owner authorized removal rather than licensing clearance. Entra connection
string authentication is consequently unsupported; SQL password and Windows
Integrated Authentication remain. No restricted broker assets may be shipped.
`dotnet run --project tools/MssqlMcp.RepoTool -- check-redistribution` remains a fail-closed release/CI guard against
reintroducing that dependency. Removing this known component is not a complete
legal/licensing audit. Native SNI asset exclusion is described below.

## Excluded Microsoft.Data.SqlClient.SNI.runtime assets

- License: Microsoft Software License Terms ("Distributable Code").
- Copyright: Copyright (c) Microsoft Corporation.
- Source: https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/7.1.2.
- Purpose: Windows native SNI. The package remains in NuGet's restore graph because
  SqlClient depends on it, but all assets are excluded from builds and publication.
- Section 3(a) permits object-code redistribution subject to additional requirements;
  framework-dependent publishing does not avoid those requirements because it
  also copies native dependencies. The earlier framework-dependent-only workaround
  did not actually exclude SNI.
- The application instead uses MIT-licensed managed SNI on every platform.
  `Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows` is enabled in
  executable/test runtime configuration. Native SNI is not distributed in our
  binaries, container, Windows archive, or NuGet tool package.
  See ADR-0002; no licensing clearance is being sought for excluded native assets.

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

## .NET 11 Runtime (self-contained builds only)

- License: MIT
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/runtime
- Purpose: .NET runtime bundled into self-contained builds (Linux x64/arm64,
  macOS x64/arm64, Windows x64). The NuGet tool package is framework-dependent and
  does not redistribute the runtime.

## Bundled license texts

Texts are copied verbatim from the upstream repositories at the versions pinned
in `Directory.Packages.props` (or, for the .NET runtime, the matching release tag).

| File in `LICENSES/` | Covers | Upstream |
|---|---|---|
| `DotNet.txt` | .NET runtime and the `Microsoft.Extensions.*` packages (MIT) | https://github.com/dotnet/runtime/blob/v11.0.0/LICENSE.TXT |
| `DotNet-ThirdParty.txt` | Notices for third-party code inside the .NET runtime libraries | https://github.com/dotnet/runtime/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT |
| `ModelContextProtocol.txt` | ModelContextProtocol C# SDK 2.4.0 (MIT to Apache-2.0 transition notice, then the Apache-2.0 text) | https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.4.0/LICENSE |
| `Microsoft.Data.SqlClient.txt` | Microsoft.Data.SqlClient 7.1.2 (MIT) | https://github.com/dotnet/SqlClient/blob/v7.1.2/LICENSE |
| `Microsoft.SqlServer.TransactSql.ScriptDom.txt` | Microsoft.SqlServer.TransactSql.ScriptDom (MIT); the pinned package's source commit is not published as a tag, so the text is the repository's current `LICENSE` | https://github.com/microsoft/SqlScriptDOM/blob/main/LICENSE |

`Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Logging.Console` 11.0.0
come from the same dotnet/runtime repository and share its MIT license and
third-party notices.
