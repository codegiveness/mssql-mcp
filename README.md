# mssql-mcp

<!-- mcp-name: io.github.codegiveness/mssql-mcp -->
[![CI](https://github.com/codegiveness/mssql-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/codegiveness/mssql-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/codegiveness.mssql-mcp)](https://www.nuget.org/packages/codegiveness.mssql-mcp)
[![npm (scoped)](https://img.shields.io/npm/v/@codegiveness/mssql-mcp)](https://www.npmjs.com/package/@codegiveness/mssql-mcp)
[![.NET](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/github/license/codegiveness/mssql-mcp)](./LICENSE)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/codegiveness/mssql-mcp/badge)](https://scorecard.dev/viewer/?uri=github.com/codegiveness/mssql-mcp)
[![OpenSSF Best Practices](https://img.shields.io/badge/OpenSSF_Best_Practices-Pending-yellow)](https://bestpractices.dev/)
[![SBOM](https://img.shields.io/badge/SBOM-CycloneDX-blue)](./docs/security-posture.md#supply-chain-attestation)
[![Security Policy](https://img.shields.io/badge/Security-Policy-blue)](./SECURITY.md)

An MCP server that lets AI agents safely query Microsoft SQL Server — read-only by default, with AST validation, transaction rollback, and timeouts.

> **Security & trust.** The server runs in **Restricted mode** (read-only) by default. Provide a **least-privilege connection string** — a SQL login that can only SELECT from the databases you want the agent to see. Secrets are passed via the `MSSQL_CONNECTION_STRING` environment variable, never command-line arguments. See [SECURITY.md](./SECURITY.md) and [docs/security-posture.md](./docs/security-posture.md) for the full security posture (OpenSSF Scorecard, SBOM, branch protection, supply-chain attestation).

## Contents

- [Quick start](#quick-start)
- [Installation](#installation)
  - [Platform matrix](#platform-matrix)
  - [How the binary is delivered](#how-the-binary-is-delivered)
  - [Docker](#docker)
  - [Windows note](#windows-note)
- [Supported clients](#supported-clients)
- [Validate it works](#validate-it-works)
- [Why this exists](#why-this-exists)
- [Access modes](#access-modes)
- [Tools](#tools)
- [Examples](#examples)
- [Authentication](#authentication)
- [Configuration](#configuration)
  - [CLI reference](#cli-reference)
- [Troubleshooting](#troubleshooting)
- [Security](#security)
- [Development](#development)
- [Trademarks & licensing](#trademarks--licensing)
- [Contributing](#contributing)
- [Stability](#stability)
- [Architecture & decisions](#architecture--decisions)

## Quick start

**1. Verify the binary runs on your machine.**

macOS / Linux:

```bash
npx -y @codegiveness/mssql-mcp --version
```

Windows (requires [.NET 10 runtime](https://dotnet.microsoft.com/download)):

```bash
dotnet tool install -g codegiveness.mssql-mcp
mssql-mcp --version
```

You should see `mssql-mcp 0.4.2`. If the version prints, the install is good. If it fails, see [Troubleshooting](#troubleshooting).

**2. Add the server to your MCP client.**

For Claude Desktop, edit `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

Replace every `<...>` placeholder with your real SQL Server details. For other clients, see [Supported clients](#supported-clients) below.

**3. Validate the connection.**

```bash
npx -y @codegiveness/mssql-mcp --validate
```

Prints `[startup] Connection validated successfully.` — you're done. See [Validate it works](#validate-it-works) for what to do if it fails.

## Installation

The Quick start above covers the common path. This section covers platform details, Docker, and Windows-specific notes.

### Platform matrix

| RID | Self-contained | Archive | Notes |
|---|---|---|---|
| `linux-x64` | yes | `.tar.gz` | `npx -y @codegiveness/mssql-mcp` just works |
| `linux-arm64` | yes | `.tar.gz` | `npx -y @codegiveness/mssql-mcp` just works |
| `osx-x64` | yes | `.tar.gz` | Intel Macs |
| `osx-arm64` | yes | `.tar.gz` | Apple Silicon |
| `win-x64` | no (framework-dependent) | `.zip` | Requires .NET 10 runtime; `dotnet tool install` is the recommended path |

Windows is framework-dependent because the self-contained build would bundle `Microsoft.Data.SqlClient.SNI` under the Microsoft "Distributable Code" license, whose anti-copyleft clause conservatively blocks redistribution under our MIT license. Linux and macOS use the managed SNI implementation (MIT-clean). This is the distribution rationale — see [Architecture & decisions](#architecture--decisions) for the full ADR.

### How the binary is delivered

The npm package uses per-platform `optionalDependencies` (`@codegiveness/mssql-mcp-<rid>`) — npm's dependency resolution installs the matching package automatically, no `postinstall` script involved. This works even with `--ignore-scripts`.

The shim (`npm/bin/mssql-mcp.js`) runs on every invocation:

1. Resolves the per-platform optional dependency via `require.resolve` and execs the binary directly (happy path).
2. If the optional dependency is absent (`--no-optional`, corporate mirrors), checks the cache at `~/.mssql-mcp/bin/<version>/<rid>/`. If cached, execs it.
3. If not cached, downloads the flat archive from the matching GitHub Release, verifies the `.sha256` sidecar, extracts, `chmod 755` (Unix), caches, and execs. Set `MSSQL_MCP_NO_DOWNLOAD=1` to skip the download attempt.

Every failure mode prints the RID, the GitHub Releases URL for manual download, and the `dotnet tool install -g codegiveness.mssql-mcp` fallback.

### Docker

A `Dockerfile` is included for containerized deployments (Linux x64, self-contained):

```bash
docker build -t mssql-mcp .
docker run --rm -e MSSQL_CONNECTION_STRING="Server=...;Database=...;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True;" mssql-mcp --validate
```

To point your MCP client at the Docker image, use `docker` as the command:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "docker",
      "args": ["run", "--rm", "-i", "-e", "MSSQL_CONNECTION_STRING", "mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

The `-i` flag keeps stdin open for the MCP stdio transport. The image must be built first: `docker build -t mssql-mcp .`

### Windows note

`npx -y @codegiveness/mssql-mcp` on Windows delivers a framework-dependent build via `optionalDependencies`. The build requires the .NET 10 runtime to be installed. If the runtime is missing, the shim prints a clear error with the [download URL](https://dotnet.microsoft.com/download) and the `dotnet tool install` fallback. If you don't want to install the runtime, install the .NET tool instead:

```bash
dotnet tool install -g codegiveness.mssql-mcp
```

## Supported clients

mssql-mcp works with any MCP-compatible client (we call them **harnesses**). Pick yours below. Every snippet is copy-paste-ready — just replace the `<...>` placeholders with your SQL Server details.

The placeholder connection string used throughout is:

```text
Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;
```

> **Windows / dotnet tool users:** If you installed via `dotnet tool install -g codegiveness.mssql-mcp`, replace `"command": "npx"` and `"args": ["-y", "@codegiveness/mssql-mcp"]` with `"command": "mssql-mcp"` (no `args`) in any snippet below.

### Claude Desktop

Edit `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Claude Code

Edit `~/.claude.json` (user-level) or `.mcp.json` in your project root:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Cursor

Edit `~/.cursor/mcp.json` (macOS/Linux) or `%USERPROFILE%\.cursor\mcp.json` (Windows):

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### VS Code / GitHub Copilot

Edit `.vscode/mcp.json` in your workspace (or `~/.vscode/mcp.json` for global):

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Windsurf

Edit `~/.codeium/windsurf/mcp_config.json`:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Cline / Roo Code

Edit `~/Library/Application Support/Code/User/globalStorage/saoudrizwan.claude-dev/settings/cline_mcp_settings.json` (VS Code extension path):

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Continue

Edit `~/.continue/config.json`:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### opencode

Edit `~/.config/opencode/opencode.json` (user-level) or `opencode.json` in your project root. opencode uses a `"mcp"` key (not `"mcpServers"`), `"command"` as an array, and `"environment"` (not `"env"`):

```text
{
  "mcp": {
    "mssql-mcp": {
      "type": "local",
      "command": ["npx", "-y", "@codegiveness/mssql-mcp"],
      "enabled": true,
      "environment": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Codex CLI

Edit `~/.codex/config.toml`:

```toml
[mcp_servers.mssql-mcp]
command = "npx"
args = ["-y", "@codegiveness/mssql-mcp"]

[mcp_servers.mssql-mcp.env]
MSSQL_CONNECTION_STRING = "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
```

### Gemini CLI

Edit `~/.gemini/settings.json`:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Antigravity IDE

Edit `~/.gemini/antigravity/mcp_config.json`:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Hermes Agent

Edit `~/.hermes/config.yaml`:

```yaml
mcp_servers:
  mssql-mcp:
    command: npx
    args:
      - "-y"
      - "@codegiveness/mssql-mcp"
    env:
      MSSQL_CONNECTION_STRING: "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
```

### Kiro

Edit `~/.kiro/settings/mcp.json`:

```jsonc
{
  "mcpServers": {
    "mssql-mcp": {
      "command": "npx",
      "args": ["-y", "@codegiveness/mssql-mcp"],
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

### Zed

Edit `~/.config/zed/settings.json` (macOS/Linux) or `%APPDATA%\Zed\settings.json` (Windows). The `mssql-mcp` server goes under `assistant.mcp_servers`:

```text
{
  "assistant": {
    "mcp_servers": {
      "mssql-mcp": {
        "command": "npx",
        "args": ["-y", "@codegiveness/mssql-mcp"],
        "env": {
          "MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
        }
      }
    }
  }
}
```

## Validate it works

After adding the config, run `--validate` from your terminal. This proves the server can start and connect to SQL Server — without needing the harness at all:

```bash
npx -y @codegiveness/mssql-mcp --validate
```

**What success looks like:**

```
[startup] Connection validated successfully.
```

Exit code 0. The binary runs, the connection string is valid, and SQL Server accepted it.

**What failure looks like:**

```
[startup] Connection validation failed [tag]: <obfuscated error>
```

Exit code 1. The `tag` tells you the category:

| Tag | Meaning | Next step |
|---|---|---|
| `timeout` | Server didn't respond in time | Check the hostname/port, firewall rules, and that SQL Server accepts TCP connections. |
| `connection` | Network-level failure (refused, DNS, etc.) | Verify the `Server=` value, that SQL Server is running, and that port 1433 (or your custom port) is reachable. |
| `auth` | Login failed | Double-check `User Id` and `Password`. If using Entra ID, verify the `Authentication=` setting. |
| `certificate` | TLS/SSL handshake failed | See [Troubleshooting: connection / login failed](#connection-failed--login-failed) below — usually `TrustServerCertificate=True` is needed for self-signed certs. |

If `--validate` passes but your harness can't see the server, the problem is in the harness config — see [Troubleshooting: agent can't see the server](#agent-cant-see-the-server).

## Why this exists

`mssql-mcp` gives AI agents a small, well-typed tool surface backed by AST validation, read-only transactions, timeouts, and a byte-size transport safety net — so an agent can explore schema, run SELECTs, and analyze query plans without a human in the loop, by default.

| Feature | mssql-mcp |
|---|---|
| Guardrails | AST validation + read-only transactions |
| Destructive SQL | Blocked by default (rollback) |
| Tool surface | 9 typed tools |
| Transport safety | Byte-size limit on stdio |
| Language | C#/.NET 10 |
| Target DB | SQL Server |

## Access modes

mssql-mcp ships in two modes, selected at startup via `--access-mode` or `MSSQL_ACCESS_MODE`:

- **Restricted (default)** — read-only. The Guard enforces an AST allowlist, wraps every query in `BEGIN TRAN ... ROLLBACK`, applies a per-query command timeout, and truncates oversized results with a notice. All tools carry `readOnlyHint=true`. This is the mode to use with AI agents.
- **Unrestricted (opt-in)** — full DML/DDL via `execute_sql`. The Guard is bypassed for `execute_sql`, destructive operations carry `destructiveHint=true`, and the default query timeout is unlimited. Use this only when the human operator has explicitly authorized schema changes or writes. `explain_query` is still Guarded in both modes (it never executes the query).

```bash
mssql-mcp --access-mode unrestricted
# or
MSSQL_ACCESS_MODE=unrestricted mssql-mcp
```

## Tools

Nine tools, all `readOnlyHint=true` in Restricted mode. `execute_sql` gains `destructiveHint=true` in Unrestricted mode.

| Tool | Description |
|---|---|
| `list_databases` | List all databases with an `is_current` flag (system DBs excluded). |
| `list_schemas` | List schemas in the current or specified database. |
| `list_objects` | List tables/views/procedures/functions with schema, type, and limit filters (default 1000). |
| `get_object_details` | Return columns, parameters, indexes, and triggers for a specific object. |
| `execute_sql` | Execute a T-SQL batch. SELECT in Restricted; DML/DDL in Unrestricted. |
| `explain_query` | Show the execution plan summary (or raw XML) without executing the query. |
| `analyze_indexes` | Missing-index analysis from `sys.dm_db_missing_index_*` DMVs (workload-wide or per-query). |
| `get_top_queries` | Top queries by CPU/duration/reads from `sys.dm_exec_query_stats`. |
| `analyze_db_health` | Summary health check: size, VLFs, fragmentation, stats staleness, blocking. |

For full input schemas and return shapes, see [ADR-0016](./docs/adr/0016-tool-input-schemas.md).

<details>
<summary>Tool parameters and return shapes</summary>

### list_databases

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| _(none)_ | | | | |

Returns: JSON array of `{name, is_current}` objects (system DBs excluded).

### list_schemas

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `database` | string | no | current db | Database to list schemas in (cross-DB-validated) |

Returns: JSON array of `{name, schema_id}` objects.

### list_objects

| Parameter | Type | Required | Default | Max | Description |
|---|---|---|---|---|---|
| `database` | string | no | current db | — | Cross-DB-validated |
| `schema` | string | no | all | — | Filter by schema name |
| `type` | enum | no | all | — | `TABLE`, `VIEW`, `PROCEDURE`, `FUNCTION` |
| `limit` | int | no | 1000 | 5000 | Protects agent context window |

Returns: JSON array of `{name, schema, type}` objects. Truncation notice prepended if limit hit.

### get_object_details

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `database` | string | no | current db | Cross-DB-validated |
| `schema` | string | **yes** | | |
| `name` | string | **yes** | | |
| `type` | enum | no | auto | Same enum as `list_objects` |

Returns: Columns, parameters, indexes, and triggers for the object. Returns `OBJECT_NOT_FOUND` error if no match.

### execute_sql

| Parameter | Type | Required | Description |
|---|---|---|---|
| `sql` | string | **yes** | Single T-SQL batch. Guard validates in Restricted mode. |

Returns: JSON array of row objects (SELECT) or status objects (DML/DDL in Unrestricted mode).

### explain_query

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `sql` | string | **yes** | | The query to get an execution plan for |
| `format` | enum | no | `summary` | `summary` or `xml` |

Returns: `summary` — JSON object with estimated cost, missing indexes, warnings, top 5 ops. `xml` — raw SHOWPLAN_XML. Never executes the query.

### analyze_indexes

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `database` | string | no | current db | Cross-DB-validated |
| `query` | string | no | none | If provided, per-query analysis; if omitted, workload-wide |

Returns: JSON array of missing-index recommendations from `sys.dm_db_missing_index_*` DMVs.

### get_top_queries

| Parameter | Type | Required | Default | Max | Description |
|---|---|---|---|---|---|
| `database` | string | no | current db | — | Cross-DB-validated |
| `order_by` | enum | no | `avg_cpu` | — | `avg_cpu`, `total_cpu`, `avg_duration`, `total_duration`, `total_logical_reads`, `execution_count` |
| `limit` | int | no | 10 | 100 | |

Returns: JSON array of top queries from `sys.dm_exec_query_stats`.

### analyze_db_health

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `database` | string | no | current db | Cross-DB-validated |

Returns: JSON array of 5 summary objects (size, VLF count, fragmentation, stats staleness, blocking).

</details>

## Examples

### list_databases

Agent calls:

```json
{"method": "tools/call", "params": {"name": "list_databases"}}
```

Server returns:

```json
[
  {"name": "mydb", "is_current": true},
  {"name": "reporting", "is_current": false}
]
```

### execute_sql

Agent calls:

```json
{"method": "tools/call", "params": {"name": "execute_sql", "arguments": {"sql": "SELECT TOP 5 id, name FROM Users"}}}
```

Server returns:

```json
[
  {"id": 1, "name": "Alice"},
  {"id": 2, "name": "Bob"},
  {"id": 3, "name": "Carol"},
  {"id": 4, "name": "Dave"},
  {"id": 5, "name": "Eve"}
]
```

### explain_query

Agent calls:

```json
{"method": "tools/call", "params": {"name": "explain_query", "arguments": {"sql": "SELECT * FROM Orders WHERE customer_id = 42"}}}
```

Server returns:

```json
{
  "estimatedTotalCost": 0.0033,
  "missingIndexes": [],
  "warnings": [],
  "topOperations": [
    {"operation": "Index Seek", "estimatedCost": 0.0033, "estimatedRows": 1, "object": "AppDb.dbo.Orders.PK_Orders"}
  ]
}
```

### analyze_db_health

Agent calls:

```json
{"method": "tools/call", "params": {"name": "analyze_db_health", "arguments": {"database": "mydb"}}}
```

Server returns:

```json
[
  {"check": "database_size", "size_mb": 2048, "log_mb": 256},
  {"check": "vlf", "vlf_count": 24},
  {"check": "index_fragmentation", "total_indexes": 150, "fragmented_gt_30pct": 12, "worst": "dbo.Orders (87%)"},
  {"check": "stats_staleness", "total_stats": 150, "stale_gt_7d": 3, "max_staleness_days": 14},
  {"check": "blocking", "blocked_sessions": 0}
]
```

## Authentication

mssql-mcp uses the connection string you provide — it does not authenticate on its own. The connection string determines the auth mode.

### SQL Server authentication (username + password)

```text
Server=tcp:myserver.database.windows.net,1433;Database=mydb;User Id=sqladmin;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=False;
```

### Windows Integrated Authentication

On Windows with .NET 10, `Integrated Security=True` uses the running process's Windows credentials:

```text
Server=myserver;Database=mydb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;
```

This does not work from the self-contained Linux/macOS npm binary (no SSPI). For cross-platform, use SQL auth or Microsoft Entra ID.

### Microsoft Entra ID (formerly Azure AD) — Default authentication

`Authentication=Active Directory Default` picks up the ambient credential — managed identity in Azure, `az login` locally, or `DefaultAzureCredential` chain:

```text
Server=tcp:myserver.database.windows.net,1433;Database=mydb;Authentication=Active Directory Default;Encrypt=True;
```

For service principals with a client secret, use `Active Directory Service Principal` with `User ID` and `Password` set to the SPN client ID and secret respectively.

## Configuration

All runtime parameters are configurable via environment variable. Precedence: CLI flag (if present) > env var > hardcoded default. The single exception is `MSSQL_CONNECTION_STRING`, which takes precedence over `--connection-string` because secrets live in env, not argv.

| Env var | Default | CLI flag | Purpose |
|---|---|---|---|
| `MSSQL_CONNECTION_STRING` | (none — required) | `--connection-string` (env wins) | SQL Server connection string |
| `MSSQL_ACCESS_MODE` | `restricted` | `--access-mode` | `restricted` or `unrestricted` |
| `MSSQL_QUERY_TIMEOUT` | `30` (restricted), `0` (unrestricted) | `--query-timeout` | Per-query command timeout in seconds; `0` = unlimited |
| `MSSQL_LOG_LEVEL` | `info` | `--log-level` | `trace` / `debug` / `info` / `warning` / `error` / `critical` |
| `MSSQL_LOG_FILE` | (stderr only) | (none) | Optional file path for log output |
| `MSSQL_LOG_FILE_MAX_BYTES` | `52428800` (50 MB) | (none) | Byte threshold for active file rotation per ADR-0030; `0` disables rotation |
| `MSSQL_LOG_FILE_MAX_ROLLS` | `3` | (none) | Number of archived files (`<path>.1`..`<path>.{n}`) retained per ADR-0030 |
| `MSSQL_MAX_RESULT_BYTES` | `10485760` (10 MB) | (none) | Result byte-size safety net; `0` disables |
| `MSSQL_RETRY_COUNT` | `3` | (none) | Transient-failure retry count (after first attempt) |
| `MSSQL_RETRY_INTERVAL` | `2` seconds | (none) | Min backoff for transient retries |
| `MSSQL_RETRY_INTERVAL_MAX` | `10` seconds | (none) | Max backoff for transient retries |

> **Connection pooling:** The server does not override SqlClient connection-string pool settings. You can set `Max Pool Size` and `Connection Lifetime` in `MSSQL_CONNECTION_STRING` if you need to. In the stdio single-agent deployment model there is one MCP server process per harness, agents call tools sequentially, and the pool realistically holds 1–2 connections, so the per-query command timeout is the backstop against pool exhaustion.

Invalid values fail fast at startup with a clear `[startup]` error naming the var, the invalid value, and the accepted range. Unknown env vars are ignored (forward compatibility).

### CLI reference

`mssql-mcp --help` prints the full usage block:

```text
Usage: mssql-mcp [options]
  (no args)              Start the MCP stdio server (default)
  --version              Print version and exit
  --help, -h             Print this help and exit
  --validate             Test the SQL Server connection and exit
  --connection-string    SQL Server connection string (env: MSSQL_CONNECTION_STRING)
  --access-mode          restricted | unrestricted (default: restricted, env: MSSQL_ACCESS_MODE)
  --query-timeout        Per-query timeout in seconds (default: 30, env: MSSQL_QUERY_TIMEOUT)
  --log-level            trace | debug | info | warning | error | critical (default: info, env: MSSQL_LOG_LEVEL)

To update mssql-mcp:
  npm install -g @codegiveness/mssql-mcp@latest
  # or
  dotnet tool update -g codegiveness.mssql-mcp
```

## Troubleshooting

### Agent can't see the server

Symptom: your harness (Claude Desktop, Cursor, etc.) doesn't list `mssql-mcp` in its tools, or the agent says it can't find the server.

Diagnosis steps:

1. **Check the config file path.** Each harness looks for config in a specific location — see [Supported clients](#supported-clients) for exact paths. A wrong path means the harness never reads your config.
2. **Check JSON syntax.** A missing comma, trailing comma, or unescaped quote silently breaks the config. Paste your JSON into a linter (e.g. `python -m json.tool config.json`) to check.
3. **Restart the harness.** Most harnesses read config only at startup. Fully quit (not just minimize) and reopen.
4. **Check that `npx` is on the harness's PATH.** Some harnesses don't inherit your shell's PATH. If `npx` isn't found, use the absolute path (run `which npx` to find it) or switch to `dotnet tool install` and use `"command": "mssql-mcp"`.
5. **Check harness logs** — see [Per-harness log locations](#per-harness-log-locations) below.

### Connection failed / login failed

Symptom: `--validate` prints `[startup] Connection validation failed [connection]: ...` or `[startup] Connection validation failed [auth]: ...`.

Common fixes:

- **`Encrypt=True` is required.** Modern SqlClient defaults to `Encrypt=True`. If your server uses a self-signed certificate (common for on-prem SQL Server), also add `TrustServerCertificate=True`. Without it you'll see a `certificate` tag error:
  ```text
  [startup] Connection validation failed [certificate]: The certificate chain was issued by an authority that is not trusted.
  ```
  Fix: add `TrustServerCertificate=True` to the connection string. For production Azure SQL, use `TrustServerCertificate=False` with a valid server certificate.

- **Login failed (`auth` tag).** Verify the `User Id` and `Password`. For Azure SQL, the username is usually `admin@server` (not `domain\user`). For on-prem SQL Server, check that SQL Server Authentication is enabled (it's off by default — enable it in SSMS > Server Properties > Security).

- **Connection refused (`connection` tag).** Check that:
  - The `Server` hostname is correct and resolvable.
  - SQL Server is running and accepts TCP connections (check `SQL Server Configuration Manager` > `Protocols for <instance>` > `TCP/IP` = Enabled).
  - Port 1433 (or your custom port) is reachable: `telnet <server> 1433` or `nc -zv <server> 1433`.
  - Firewall rules allow the connection.

- **Timeout (`timeout` tag).** The default connection timeout is 15 seconds. If your server is slow or far away, add `Connect Timeout=30` to the connection string.

### Windows .NET runtime missing

Symptom: On Windows, `npx -y @codegiveness/mssql-mcp --version` fails with an error mentioning .NET or the runtime.

The Windows build is framework-dependent — it needs the .NET 10 runtime installed. Fix:

1. Install the [.NET 10 runtime](https://dotnet.microsoft.com/download).
2. Verify: `dotnet --version` should print `10.x.x`.
3. Re-run `npx -y @codegiveness/mssql-mcp --version`.

Alternatively, skip the runtime entirely by using the .NET tool:

```bash
dotnet tool install -g codegiveness.mssql-mcp
mssql-mcp --version
```

Then use `"command": "mssql-mcp"` (instead of `"command": "npx"`) in your harness config.

### npm self-heal download failed

Symptom: The npm package's optional dependency for your platform wasn't installed (corporate mirror, `--no-optional`, etc.), and the shim's fallback download from GitHub Releases failed.

The shim prints the RID, the GitHub Releases URL, and the fallback command. Manual fix:

1. Download the archive for your platform from the [GitHub Releases page](https://github.com/codegiveness/mssql-mcp/releases) (look for `mssql-mcp-<version>-<rid>.tar.gz` or `.zip`).
2. Extract it and note the path to the `mssql-mcp` (or `mssql-mcp.exe`) binary.
3. Option A — cache it where the shim expects:
   ```bash
   mkdir -p ~/.mssql-mcp/bin/0.4.2/<rid>
   # Copy the extracted binary there, then:
   npx -y @codegiveness/mssql-mcp --version
   ```
   Replace `<rid>` with your platform RID from the [platform matrix](#platform-matrix) (e.g. `linux-x64`, `osx-arm64`).
4. Option B — use `dotnet tool install -g codegiveness.mssql-mcp` as the fallback (cross-platform, no download needed from GitHub Releases).
5. To skip the download attempt entirely, set `MSSQL_MCP_NO_DOWNLOAD=1` in the environment. The shim will fail immediately instead of trying to download.

### Per-harness log locations

When a harness fails to start the server, check its logs:

| Harness | Log location |
|---|---|
| Claude Desktop | macOS: `~/Library/Logs/Claude/mcp*.log`. Windows: `%APPDATA%\Claude\logs\mcp*.log` |
| Claude Code | `~/.claude/logs/` (or run `claude --mcp-debug`) |
| Cursor | `~/.cursor/logs/` or Developer Tools (Help > Open Developer Tools) |
| VS Code / Copilot | Output panel (View > Output > select "MCP" or "GitHub Copilot") |
| Windsurf | `~/.codeium/windsurf/logs/` |
| Cline / Roo Code | VS Code Output panel (select "Cline" or "Roo Code") |
| Continue | `~/.continue/logs/` |
| opencode | `~/.local/share/opencode/log/` |
| Codex CLI | `~/.codex/log/` |
| Gemini CLI | `~/.gemini/logs/` or run with `GEMINI_LOG=debug` |
| Antigravity IDE | `~/.gemini/antigravity/logs/` |
| Hermes Agent | `~/.hermes/logs/` |
| Kiro | `~/.kiro/logs/` |
| Zed | `~/.local/share/zed/logs/` (or `~/.cache/zed/logs/`) |

> **Tip:** The server writes all its own logging to **stderr**. stdout is reserved for the MCP JSON-RPC protocol. If a harness captures stderr, you'll see `[startup]` messages and `[tool]` execution logs there. You can also set `MSSQL_LOG_FILE=/tmp/mssql-mcp.log` to write logs to a file for easier debugging.

## Security

### Default is read-only

In Restricted mode (the default), every `execute_sql` call is wrapped in `BEGIN TRAN ... ROLLBACK`. Even if an agent crafts a destructive statement, the Guard rejects it before it reaches SQL Server, and even if the Guard allowed it, the transaction would roll back. Defense in depth.

### Guard layers

The Guard applies four layers of validation in Restricted mode:

1. **AST allowlist** — T-SQL is parsed by ScriptDom into an AST. A Visitor walks every batch and every nested statement, rejecting anything that isn't a SELECT (or a SELECT-adjacent statement on the allowlist). `INTO`, `OPENROWSET(BULK)`, `EXECUTE`, DDL, and DML are all rejected.
2. **Read-only transaction** — every query runs inside `BEGIN TRAN ... ROLLBACK`. Even a Guard bypass can't commit changes.
3. **Command timeout** — default 30s in Restricted mode. Runaway queries are killed.
4. **Byte-size safety net** — results over 10 MB (configurable) are truncated with a notice appended, so an agent's context window isn't blown out.

### Transaction rollback

In Unrestricted mode, the transaction wrapper is removed — that's what makes DML/DDL work. `explain_query` is still Guarded in both modes because it uses `SET SHOWPLAN_XML ON` and never executes the query.

### Reporting vulnerabilities

See [SECURITY.md](./SECURITY.md). Do not open a public issue for security vulnerabilities — use GitHub's private vulnerability reporting.

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- (Optional) Docker — for integration tests against Azure SQL Edge
- (Optional) Node 18+ — to run `npm/test.js` smoke tests

### Build and test

```bash
git clone https://github.com/codegiveness/mssql-mcp.git
cd mssql-mcp
dotnet restore
dotnet build
dotnet test --filter Category!=Integration
```

This is what CI runs. ~414 tests, completes in seconds.

### Integration tests (requires live SQL Server)

```bash
# Start Azure SQL Edge container
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name mssql-edge -d mcr.microsoft.com/azure-sql-edge:latest

# Run integration tests
INTEGRATION=true MSSQL_CONNECTION_STRING="Server=localhost;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=True;" dotnet test
```

Integration tests are tagged `[Trait("Category", "Integration")]` and skipped by default.

### npm smoke test

```bash
node npm/test.js
```

Verifies the shim (`bin/mssql-mcp.js`) parses, the RID mapping returns expected values for known platforms, and the checksum parser handles bare and `sha256sum`-formatted sidecar files. The real integration test is `npm pack && npm install --ignore-scripts` on each platform.

### Project layout

```
mssql-mcp.sln
src/
  mssql-mcp.Core/       # Guard, SqlExecutor, Options — no MCP deps
  mssql-mcp.Tools/      # 9 tool classes with [McpServerTool]
  mssql-mcp/            # App: Program.cs, DI, stdio, CLI, npm entrypoint
tests/
  mssql-mcp.Core.Tests/ # Guard AST validation, type coercion, etc.
  mssql-mcp.Tools.Tests/ # Tool attribute wiring, schema tests
npm/                    # npm package: bin shim + per-platform packages + smoke test
docs/adr/               # Architectural Decision Records
```

See [CONTRIBUTING.md](./CONTRIBUTING.md) for coding standards, ADR workflow, and the PR process.

## Trademarks & licensing

- **License:** MIT. See [LICENSE](./LICENSE) and [NOTICE](./NOTICE).
- **Third-party notices:** See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) for the full list, including `Microsoft.Data.SqlClient.SNI`'s "Distributable Code" license terms (which is why Windows builds are framework-dependent).
- **Not affiliated with Microsoft Corporation.** "Microsoft SQL Server", "Windows", "Azure", "Microsoft Entra ID", and related marks are trademarks of Microsoft Corporation. This project is an independent MCP server that connects to SQL Server using the public `Microsoft.Data.SqlClient` ADO.NET provider.
- **Client Access License (CAL) / multiplexing.** Using mssql-mcp does not reduce or eliminate SQL Server licensing requirements. Each end user or device that indirectly accesses SQL Server through mssql-mcp may require a CAL or a Core-based license, exactly as if they were connecting directly. You are responsible for ensuring your SQL Server deployment is properly licensed.

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md). PRs welcome — please open an issue first to discuss the scope of any non-trivial change.

## Stability

mssql-mcp is currently `0.x`. The tool surface is stable (tool names, parameter names, and parameter types don't break within the `0.x` series); CLI flags, env var names, error response shapes, and return value formats may change between minor versions before `1.0.0`.

## Architecture & decisions

Architectural Decision Records (ADRs) document every significant design choice. They live in [`docs/adr/`](./docs/adr/). See the [ADR index](./docs/adr/README.md) for all 33 ADRs. Key decisions:

- [ADR-0006](./docs/adr/0006-guard-ast-allowlist.md) — Guard AST allowlist
- [ADR-0015](./docs/adr/0015-configuration-via-env-vars.md) — Configuration via env vars (secrets in env, not argv)
- [ADR-0028](./docs/adr/0028-binary-delivery-via-optional-dependencies-and-shim-self-heal.md) — Binary delivery via optional dependencies and shim self-heal
- [ADR-0033](./docs/adr/0033-branch-protection-posture-for-solo-maintained-project.md) — Branch protection posture for solo-maintained project

For the consolidated security posture (OpenSSF Scorecard, SBOM, supply-chain attestation, branch protection), see [docs/security-posture.md](./docs/security-posture.md).
