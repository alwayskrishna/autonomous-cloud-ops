# Local MCP stdio server (PR #25)

Run `dotnet run --project src/AutonomousCloudOps.Mcp/AutonomousCloudOps.Mcp.csproj` and communicate using newline-delimited JSON-RPC 2.0 over stdin/stdout. Supported methods: `initialize`, `ping`, `tools/list`, `tools/call`; notifications are ignored. Tool schemas and execution are delegated to the existing read-only diagnostic tool registry.

This is a **minimal MCP stdio implementation**, not yet certified against MCP SDK conformance tests. It has no authentication, session negotiation, resources, prompts, cancellation handling, or robust JSON-RPC error handling. It runs offline and does not execute recovery. The incident store is empty at startup unless populated by future integration.
