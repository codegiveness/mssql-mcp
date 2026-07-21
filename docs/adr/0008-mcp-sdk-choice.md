# MCP SDK choice: official `ModelContextProtocol` 1.4.1

Use the official C# MCP SDK (`ModelContextProtocol` NuGet package v1.4.1, repo `github.com/modelcontextprotocol/csharp-sdk`) rather than rolling our own JSON-RPC over stdio or adopting a 2.0 preview. The SDK is maintained in collaboration with Microsoft, ships 19M downloads, supports .NET 10 natively, and exposes tool annotations (`ReadOnly`, `Destructive`) as first-class attribute properties — exactly what our Restricted/Unrestricted mode split needs. stdio transport is one line (`.WithStdioServerTransport()`), DI matches .NET 10 idioms, license is Apache-2.0 (compatible with our MIT).

## Considered Options

- **A. Official SDK, pin to 1.4.1 stable** ✅ — chosen
- B. Official SDK, float to latest 1.x — rejected: patch churn, non-reproducible builds
- C. Official SDK, 2.0.0-preview.3 — rejected: we're stdio-only and don't need the `2026-07-28` per-request-metadata protocol; preview = churn risk
- D. Roll our own JSON-RPC over stdio — rejected: scope creep; we'd maintain framing, handshake, schema generation, and annotations ourselves for zero v1 value
- E. Community C# MCP SDK — rejected: none of significance exists

## Consequences

- Every tool method carries `[McpServerTool(ReadOnly = ..., Destructive = ...)]` — Restricted tools must explicitly set `ReadOnly = true, Destructive = false` because the SDK defaults `Destructive = true` per MCP spec.
- We depend on `Microsoft.Extensions.Hosting` for DI (already a .NET 10 idiom).
- Bumping the SDK is a deliberate, reviewed action — we read release notes before upgrading.
- v2.0 protocol features (per-request metadata) remain available as a future additive upgrade with no stdio breakage.
