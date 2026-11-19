## Problem

<!-- What user-visible problem does this solve? Link the issue (Fixes #NN) if there is one. -->

## Behavior change

<!-- What behaves differently after this PR? "None" is a valid answer for refactors and docs. -->

## Compatibility and security impact

<!-- Configuration, defaults, output formats, permissions, Restricted-mode boundaries, install channels (npm/npx, .NET tool, release archives), workflows, release or supply chain. Say "None" if none. -->

## Evidence

<!-- Commands and scenarios you ran, and what they showed (e.g. `mssql-mcp --version`, `mssql-mcp --validate` with MSSQL_CONNECTION_STRING=<placeholder>). List what you did not verify.
     A local run is not proof of hosted CI, registry publication or release availability. -->

## Authorship

<!-- Human, or the AI agent that wrote it. -->

## Checklist

- [ ] Title follows [Conventional Commits](https://github.com/codegiveness/mssql-mcp/blob/main/CONTRIBUTING.md#commit-conventions)
- [ ] [Pre-push checklist](https://github.com/codegiveness/mssql-mcp/blob/main/CONTRIBUTING.md#pre-push-checklist) passes (unit and integration tests, format check, README linter, version consistency, MCP stdio smoke test)
- [ ] Affected docs, `--help` output and ADRs updated
- [ ] No credentials, real connection strings, customer SQL or database results anywhere in the change or this description
- [ ] Workflow changes (if any) pin actions to full commit SHAs and keep least-privilege permissions
