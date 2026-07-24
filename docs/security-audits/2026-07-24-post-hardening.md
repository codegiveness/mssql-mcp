# Security Research Result — Post-Hardening Audit

## Verdict

**PASS WITH FINDINGS** — 2 confirmed credential-leak paths fixed, 1 cross-DB documentation gap documented, 1 falsified finding, 3 accepted-risk/design notes.

## Scope

- **Target:** mssql-mcp post-hardening (commits b162a3e, b3e1a20)
- **Base/diff:** `148ddf5..b3e1a20` — 9 files changed (Scorecard workflow, CycloneDX SBOM, Dockerfile, .dockerignore, server.json, security-posture.md, README badges, ci.yml, release.yml)
- **Full-repo audit:** All security-sensitive C# source (SqlGuard, SqlTools, ToolErrors, PasswordObfuscator, ConnectionValidator, SqlExecutor, StatementClassifier, Program.cs, MssqlMcpOptions), npm shim (bin/mssql-mcp.js), Dockerfile, workflows
- **Commands run:** `dotnet test --filter Category!=Integration` (414 passed, 4 skipped), `dotnet format --verify-no-changes`, static source verification via codegraph, 13 PoC tests (SH-5 falsification), 2 regression tests (AHD-2/AHD-3)

## Team

| Member | Role | Category |
|--------|------|----------|
| surface-hunter | Attack surface mapping | deep |
| auth-data-hunter | Auth/injection/credential hunting | ultrabrain |
| runtime-supply-hunter | Filesystem/subprocess/supply-chain hunting | unspecified-high |
| poc-engineer-a | PoC construction + falsification | unspecified-high |
| poc-engineer-b | Independent PoC reproduction | deep |

## Findings

| Severity | Title | CWE | Exploitability | Impact | PoC | Fix |
|----------|-------|-----|----------------|--------|-----|-----|
| Medium | Credential leak via SqlErrorOrConnection transient path (AHD-2) | CWE-532, CWE-209 | Transient SqlException → unobfuscated ex.Message to Agent | Password/login in Agent context | Confirmed via source + regression test | Fixed: PasswordObfuscator.Obfuscate inside ConnectionError (boundary defense-in-depth) |
| Low-Medium | Credential leak via ToolErrors.Internal (AHD-3) | CWE-209 | Generic Exception → unobfuscated ex.Message to Agent | Password in Agent context | Confirmed via source + regression test | Fixed: PasswordObfuscator.Obfuscate(ex.Message) |
| Medium (High multi-tenant) | Cross-DB read via 3-part names in Restricted mode (AHD-1) | CWE-639, CWE-732 | Agent supplies `SELECT * FROM OtherDb.dbo.Users` — Guard accepts, rows returned before ROLLBACK | Tenant data isolation bypass | Confirmed via source + PoC test | Documented: Restricted = statement-type safety, not data-scope safety |
| Low | npm redirect MITM (SH-4) | CWE-601 | Requires breaking TLS on github.com — initial URL hardcoded | Malicious binary substitution | Statically confirmed, downgraded | Accepted risk (requires TLS breach) |
| N/A | QuoteIdentifier convention break (SH-5) | — | — | — | **FALSIFIED** — 13 PoC tests prove brackets structurally unclosable | No fix needed |
| Low (defense-in-depth) | StatementClassifier parse failure coupling (AHD-4) | CWE-754 | Parse failure → empty list → refuse execution (safe today) | Latent: undocumented contract | Confirmed via source | Document contract: parse failure → refuse execution |
| Low (design note) | Log file path injection (SH-6) | — | MSSQL_LOG_FILE from env without path validation | Local-only, low impact | Confirmed via source | Accepted risk (local operator controls env) |
| Context-dependent | Unrestricted mode xp_cmdshell RCE (SH-1) | — | Requires Unrestricted mode + permissive SQL login | RCE via xp_cmdshell | Confirmed via source | By design — documented in Unrestricted mode |

## Finding Details

### AHD-2: Credential leak via SqlErrorOrConnection (FIXED)

- **Evidence:** `src/mssql-mcp.Tools/ToolErrors.cs:261` — `return ConnectionError($"{ex.Message} Retries exhausted.");` — no `PasswordObfuscator.Obfuscate()` call. Raw SqlException message flowed to Agent via MCP JSON-RPC.
- **Attack path:** Transient connection error (4060, 40613, 40501, etc.) → SqlException with connection-string fragment in Message → `SqlErrorOrConnection` → `ConnectionError` → unobfuscated `ex.Message` in CallToolResult TextContent → Agent context.
- **PoC:** `PocLeakTests2.AHD2_TransientSqlException_ObfuscatesPasswordInConnectionError` — regression test verifies password is now obfuscated.
- **Severity rationale:** Medium — requires a transient error (network blip, SQL restart) but the password fragment reaches the AI Agent's conversation context, which may be logged or transcribed.
- **Minimal fix:** `PasswordObfuscator.Obfuscate` is now applied inside `ConnectionError(string detail)` itself (defense-in-depth at the boundary), protecting all callers uniformly.
- **Regression check:** `PocLeakTests2.AHD2_TransientSqlException_ObfuscatesPasswordInConnectionError` — asserts `Password=***;` appears, raw `Password=Hunter2!;` does not.

### AHD-3: Credential leak via ToolErrors.Internal (FIXED)

- **Evidence:** `src/mssql-mcp.Tools/ToolErrors.cs:294` — `Detail = ex.Message` — no `PasswordObfuscator.Obfuscate()` call. Raw exception message flowed to Agent.
- **Attack path:** Non-SqlException thrown in tool path (InvalidOperationException, ArgumentException wrapping connection state) → `catch (Exception ex)` → `ToolErrors.Internal(ex)` → unobfuscated `ex.Message` in InternalErrorPayload → Agent context.
- **PoC:** `PocLeakTests2.AHD3_GenericException_ObfuscatesPasswordInInternalError` — regression test verifies password is now obfuscated.
- **Severity rationale:** Low-Medium — internal exceptions are rarer than transient SqlExceptions, but same class of credential leak if the exception message contains a connection-string fragment.
- **Minimal fix:** One-line change: `PasswordObfuscator.Obfuscate(ex.Message)` in `Internal(Exception ex)`. (The `ConnectionError` path is protected at the boundary — see AHD-2.)
- **Regression check:** `PocLeakTests2.AHD3_GenericException_ObfuscatesPasswordInInternalError` — asserts `Password=***;` appears, raw `Password=Secret123;` does not.

### AHD-1: Cross-DB read via 3-part names in Restricted mode (DOCUMENTED)

- **Evidence:** `src/mssql-mcp.Core/Guard/SqlGuard.cs:280` — `if (node.Identifiers.Count >= 4)` only rejects 4-part (linked-server) names. 3-part names like `OtherDb.dbo.Users` pass the Guard.
- **Attack path:** Agent calls `execute_sql` with `SELECT * FROM SecretDb.dbo.Users` → Guard wraps in `BEGIN TRAN ... ROLLBACK` → SELECT executes and returns rows to Agent → ROLLBACK fires after data is already in Agent context.
- **PoC:** `PocLeakTests2.AHD1_SqlGuard_AcceptsThreePartName_CrossDbReadInRestricted` — confirms Guard accepts 3-part name. Contrast test confirms 4-part names are rejected.
- **Severity rationale:** Medium (High in multi-tenant) — the transaction wrapper only prevents writes, not reads. An Agent can read any database the SQL login can access. This is SQL Server's normal cross-DB behavior — the gap is that "Restricted mode" sounds like it restricts data scope, but it only restricts statement types.
- **Remediation:** Document in SECURITY.md and README that Restricted mode = statement-type safety, NOT data-scope safety. Tenant isolation must be enforced at the SQL principal permission level (least-privilege logins per tenant). A code fix (reject 3-part names) would break legitimate same-instance cross-DB queries.
- **Regression check:** `PocLeakTests2.AHD1_SqlGuard_AcceptsThreePartName_CrossDbReadInRestricted` — documents the current behavior.

### SH-5: QuoteIdentifier convention break (FALSIFIED)

- **Evidence:** `SqlHelpers.QuoteIdentifier` doubles every `]` and wraps in `[...]`.
- **PoC:** `SqlInjectionPoCTests.cs` — 13 tests across 6 malicious inputs prove brackets are structurally unclosable.
- **Verdict:** Not a vulnerability. QuoteIdentifier correctly neutralizes all bracket-escape injection attempts.

## Downgraded or Rejected Candidates

| Candidate | Reason |
|-----------|--------|
| SH-5 (QuoteIdentifier injection) | FALSIFIED — 13 PoC tests prove brackets cannot be escaped |
| SH-4 (npm redirect MITM) | Downgraded from Medium to Low — initial URL hardcoded to github.com, requires breaking TLS |
| SH-1 (Unrestricted xp_cmdshell) | Accepted risk — by design in Unrestricted mode, documented |
| AHD-4 (StatementClassifier coupling) | Defense-in-depth note — safe today, contract should be documented |
| SH-6 (Log file path injection) | Design note — local-only, operator controls env |

## Residual Risk

- **AHD-1 (cross-DB read):** Not fixed in code — documentation only. If an operator deploys mssql-mcp in Restricted mode with a SQL login that has cross-database permissions, an Agent can read from any accessible database. Mitigation: use least-privilege logins scoped to a single database.
- **PasswordObfuscator scope:** Only matches `Password=...;`, `PWD=...;`, `AccessToken=...;`, `Token=...;` patterns. Does NOT scrub login names, server names, or other connection-string fragments that may appear in exception messages. A SqlException like "Login failed for user 'sa'" still echoes the login name to the Agent.
- **PasswordObfuscator regex timeout fallback:** If a pathological exception message exceeds the 200ms regex timeout, `Obfuscate()` returns the original unobfuscated string. Extremely unlikely in practice (defense-in-depth timeout), but operators should know obfuscation ≠ full redaction.
- **npm shim redirect:** `fetchUrl` follows up to 3 redirects without host pinning. A TLS breach on github.com (or a compromised DNS) could redirect both archive and checksum to an attacker host. Accepted risk — requires breaking TLS.
- **Unrestricted mode:** By design, Unrestricted mode allows any SQL including `xp_cmdshell`. This is documented in the README and SECURITY.md. Operators must not grant Unrestricted mode to untrusted Agents.
- **No live SQL Server testing:** All PoCs used mocked executors (NSubstitute). Integration tests requiring a live SQL Server were not run. The fixes are verified at the unit level via regression tests that mock the executor and verify the error payload.
