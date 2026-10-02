# OpenSSF Best Practices Self-Assessment

The draft passing-level answers and their evidence are maintained in [`.bestpractices.json`](../.bestpractices.json), the service's [supported repository proposal format](https://github.com/ossf/best-practices-badge/blob/main/docs/bestpractices-json.md). This is the canonical assessment source, not a granted badge or a guarantee against vulnerabilities. [Issue #140](https://github.com/codegiveness/mssql-mcp/issues/140) tracks the actual external submission/result.

## Import and review

1. Open the maintainer's [project 15156 passing assessment](https://www.bestpractices.dev/en/projects/15156/passing), whose repository URL is `https://github.com/codegiveness/mssql-mcp`.
2. Once this file is on `main`, select **Save (and continue) 🤖** in the project editor to rerun the service's repository automation. The JSON supplies proposed answers; it does not submit or certify the project independently.
3. Review each proposal against the [actual passing criteria](https://www.bestpractices.dev/en/criteria/0), current release artifacts and [observed execution evidence](security-quality-follow-up.md). Revise `.bestpractices.json` when evidence changes, rather than maintaining a second Markdown answer table.
4. Resolve unknown mandatory answers honestly and save the reviewed assessment. The README uses the service's live project badge, including its in-progress state; displaying that badge does not claim passing.

The `?` statuses are deliberate, not unimplemented engineering placeholders. The service ignores unknown proposals rather than overwriting an existing answer. Developer secure-design/common-error knowledge needs personal confirmation from a primary developer. TLS key-length, cipher-weakness and forward-secrecy claims need deployment-specific evidence; library delegation does not establish them or make them inapplicable. The shim's SHA-256 and HTTPS library calls are applicable cryptography and are no longer incorrectly marked N/A.

## Access and certification limits

The maintainer supplied project **15156** after the earlier repository-URL lookup found no entry. Its public JSON identifies this repository and reports **19% passing completion**, `badge_level: in_progress`, and no achieved-passing timestamp. Most engineering criteria are still unknown in that initial saved record; the homepage URL is empty. A fresh attempt to open this exact project in the authenticated browser relay still reported its extension disconnected. No session cookies/tokens were copied, login bypass attempted, external answers saved or passing award claimed.

Repository controls and a truthful proposal file are reachable engineering work. Account-owned answers, an accessible authenticated session and the service's actual result remain prerequisites for finishing #140. Independent human review and maintenance history are separate Scorecard limitations; this proposal file cannot create them.
