# OpenSSF Best Practices Self-Assessment

The draft passing-level answers and their evidence are maintained in [`.bestpractices.json`](../.bestpractices.json), the service's [supported repository proposal format](https://github.com/ossf/best-practices-badge/blob/main/docs/bestpractices-json.md). This is the canonical assessment source, not a granted badge or a guarantee against vulnerabilities. [Issue #140](https://github.com/codegiveness/mssql-mcp/issues/140) tracks the actual external submission/result.

## Import and review

1. Open or create the maintainer-controlled project at [bestpractices.dev](https://www.bestpractices.dev/) with repository URL `https://github.com/codegiveness/mssql-mcp`.
2. Once this file is on `main`, select **Save (and continue) 🤖** in the project editor to rerun the service's repository automation. The JSON supplies proposed answers; it does not submit or certify the project independently.
3. Review each proposal against the [actual passing criteria](https://www.bestpractices.dev/en/criteria/0), current release artifacts and [observed execution evidence](security-quality-follow-up.md). Revise `.bestpractices.json` when evidence changes, rather than maintaining a second Markdown answer table.
4. Resolve unknown mandatory answers honestly, save the reviewed assessment, and retain the actual project/result URL. Only then replace the README's pending badge with the service's real badge URL.

The `?` statuses are deliberate, not unimplemented engineering placeholders. The service ignores unknown proposals rather than overwriting an existing answer. Developer secure-design/common-error knowledge needs personal confirmation from a primary developer. TLS key-length, cipher-weakness and forward-secrecy claims need deployment-specific evidence; library delegation does not establish them or make them inapplicable. The shim's SHA-256 and HTTPS library calls are applicable cryptography and are no longer incorrectly marked N/A.

## Access and certification limits

On 2026-10-02 the public project-URL lookup returned no registered project for this repository. The authenticated browser relay reported that its extension was not connected; a separate managed-browser attempt earlier timed out. Linking a GitHub account does not by itself expose that browser session to the assistant or establish a project/result. No session cookies/tokens were copied, login bypass attempted, external assessment submitted or award claimed.

Repository controls and a truthful proposal file are reachable engineering work. Account-owned answers, an accessible authenticated session and the service's actual result remain prerequisites for finishing #140. Independent human review and maintenance history are separate Scorecard limitations; this proposal file cannot create them.
