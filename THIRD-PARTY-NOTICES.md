# Third-Party Notices

This file lists third-party software distributed with mssql-mcp, along with
their license terms. Source links are provided for verification.

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

## Removed Entra authentication integration

`Microsoft.Data.SqlClient.Extensions.Azure` and its Azure.Identity / MSAL
authentication graph are no longer included. Their transitive
`Microsoft.Identity.Client.NativeInterop` 0.20.6 package has Microsoft Software
License Terms, not MIT; section 3(e) prohibits sharing/publishing/distributing
the software. Package: https://www.nuget.org/packages/Microsoft.Identity.Client.NativeInterop/0.20.6.

The owner authorized removal rather than licensing clearance. Entra connection
string authentication is consequently unsupported; SQL password and Windows
Integrated Authentication remain. No restricted broker assets may be shipped.
`scripts/check-redistribution.js` remains a fail-closed release/CI guard against
reintroducing that dependency. Removing this known component is not a complete
legal/licensing audit. Native SNI asset exclusion is described below.

## Excluded Microsoft.Data.SqlClient.SNI.runtime assets

- License: Microsoft Software License Terms ("Distributable Code").
- Copyright: Copyright (c) Microsoft Corporation.
- Source: https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/7.1.0.
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

## .NET 10 Runtime (self-contained builds only)

- License: MIT
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/runtime
- Purpose: .NET runtime bundled into self-contained builds (Linux x64/arm64,
  macOS x64/arm64). Windows builds are framework-dependent and do not
  redistribute the runtime.
