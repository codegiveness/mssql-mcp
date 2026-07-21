# Third-Party Notices

This file lists third-party software distributed with mssql-mcp, along with
their license terms. Source links are provided for verification.

## Microsoft.Data.SqlClient

- License: MIT
- Copyright: Copyright (c) .NET Foundation and Contributors
- Source: https://github.com/dotnet/SqlClient
- Purpose: ADO.NET data provider for Microsoft SQL Server. Used in all build
  configurations (NuGet package and self-contained binaries).

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
