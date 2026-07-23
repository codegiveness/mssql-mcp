# Zero-config hero command and `--validate` placement

The README hero command must be a Zero-Config Proof Command — a command that exits 0 on a clean machine with only the tool installed, no configuration required. `--validate` is demoted out of the hero position and placed after the config step, where a connection string exists. Connection-string placeholders in the config snippet use angle brackets to make them impossible to paste as-is.

## Context

The README hero presented `npx -y @codegiveness/mssql-mcp --validate` as the one-command get-started. The surrounding copy read:

> **Get started in one command:**
> ```bash
> npx -y @codegiveness/mssql-mcp --validate
> ```

`--validate` is a connection validator (ADR-0021, `ConnectionValidator.cs`): it opens a real SQL Server connection, runs `SELECT 1`, and exits 0/1. By design, it requires `MSSQL_CONNECTION_STRING` to do anything useful — `MssqlMcpOptions.Parse` throws `[startup] Missing SQL Server connection string` before `--validate` ever runs.

The result: a first-time user who copies the hero command on a clean machine gets:

```
[startup] Missing SQL Server connection string. Set MSSQL_CONNECTION_STRING env var or pass --connection-string.
```

The command is copy-pasteable but cannot succeed. This violates the Onboarding Surface's own promise ("the first two layers are copy-pasteable with zero reading"). A command that copies cleanly but errors is worse than no command — it teaches the user the tool is broken. The error message itself is competent (labeled `[startup]`, names both the env var and the CLI flag, points at the exact remediation), but the *framing* of the hero one-liner is the defect.

The goal stated by the maintainer: "the most easiest and effective MCP to install" — where even a user who reads nothing but the README hero can install and operate the MCP. The current hero fails that goal for the majority case (user has not yet set a connection string).

## Decision

### 1. Hero command is `--version`, not `--validate`

The README hero becomes:

```bash
npx -y @codegiveness/mssql-mcp --version
# → mssql-mcp 0.x.x
```

`--version` already exists (`Program.cs:14-19`): prints the assembly version and exits 0, no DB connection required. If the binary is missing, the shim fails with a clear download error (ADR-0028), so `--version` passing does prove the binary resolved. It is a true Zero-Config Proof Command (see `CONTEXT.md`).

### 2. `--validate` placed after the config step

`--validate` appears as step 3 of the Quick start, after the user has pasted the config snippet (step 2) containing a connection string. This is where a connection validator belongs.

### 3. Connection-string placeholders use angle brackets

The config snippet in step 2 uses angle-bracket placeholders instead of a real-looking example:

```jsonc
"MSSQL_CONNECTION_STRING": "Server=<your-server>;Database=<your-database>;User Id=<your-username>;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;"
```

Angle brackets are the universal "fill me in" signal. A user who pastes this as-is gets a SqlClient parse error on `<your-server>` — obviously a placeholder, not a real hostname. The real example (`Server=localhost;Database=WideWorldImporters;...`) stays in the Authentication section as a reference.

### 4. Quick start heading is "Quick start", 3 numbered steps

The heading "Get started in one command" is replaced with "Quick start" and restructured into three numbered steps: install proof (`--version`) → configure (config snippet with placeholders) → validate (`--validate`). Each step has its own command and one-line expectation.

### 5. Unified flow with a Windows callout

The Quick start is a single 3-step flow using `npx`. Immediately below step 1, a callout addresses Windows users without .NET 10:

```markdown
> **Windows without .NET 10?** Install as a .NET tool instead:
> ```bash
> dotnet tool install -g codegiveness.mssql-mcp
> mssql-mcp --version
> ```
```

This keeps the 3-step flow unified for the majority case (macOS/Linux/Windows-with-.NET) while giving Windows-without-.NET users an actionable path exactly where they'd hit the wall.

### 6. "Verify it works" section repurposed

The "Verify it works" section (ADR-0025 section 4) is repurposed to verify the *agent* can see the server — the end-to-end proof the user actually cares about. Previously it duplicated `--validate` (now step 3 of Quick start). New content: restart your harness, ask it "what databases do I have?", expect a `list_databases` tool call. This fills the `<!-- TODO: #38 -->` placeholder and gives the user a fourth proof — that the MCP wiring is visible to the agent, not just that the server starts.

## Considered Options

- **A. Swap hero to `--version`, `--validate` after config ✅** — chosen. Every command in the flow succeeds as written. `--version` is a true Zero-Config Proof Command. `--validate` appears where it can actually succeed. Minimal change (README surgery, no code change).

- **B. Keep `--validate` in hero, add a full connection-string template** — rejected. Makes the hero a wall of text. The user would need to paste a fake connection string to `localhost` that doesn't exist on their machine, so `--validate` exits 1 with a connection error — now the *error* is expected, which is confusing in a different way.

- **C. Make `--validate` degrade to install-only proof when no connection string is present** — rejected. Conflates two different verifications (install vs. connection). Weakens the meaning of `--validate` — a passing `--validate` would no longer prove the connection works. ADR-0021's definition of "100% worked" (A+B+C) explicitly requires the connection proof, so degrading it breaks that contract.

- **D. Keep `--validate` in hero, document the connection-string requirement inline** — rejected. The hero position is not the place to introduce prerequisites. A user who reads only the hero should get a win; a user who needs to read prose before the hero works is not experiencing "zero reading."

## Consequences

- **`--version` becomes the documented install proof.** It is surfaced in every install path's docs as the first command a user runs. `--validate` remains the connection proof, documented after the config step.
- **The hero command always exits 0 on a clean machine** (macOS/Linux/Windows-with-.NET). A user who reads nothing but the hero gets a success signal. This is the core fix.
- **`--validate`'s meaning is preserved.** It still means "open a connection, run SELECT 1, exit 0/1" — no degradation, no dual-mode behavior. ADR-0021's A+B+C contract is intact.
- **Connection-string placeholders force action.** A user cannot paste the config snippet as-is and expect it to work; the angle brackets signal "replace me." The real example stays in Authentication as a reference.
- **"Verify it works" section gains real content.** The `<!-- TODO: #38 -->` placeholder is filled. The section now covers the agent-visibility proof (ADR-0021's "D — first query returns"), which was previously out of scope for the server but is the proof the user actually wants.
- **ADR-0025's section order is preserved.** The 15-section structure is unchanged; this ADR only changes the content of sections 2 (Quick start) and 4 (Verify it works).
- **Windows users without .NET 10 still hit a wall** at step 1 if they ignore the callout. The shim's existing error message (ADR-0028) is the fallback — it prints the runtime download URL and the `dotnet tool install` fallback. This is an honest failure, not a silent one.
