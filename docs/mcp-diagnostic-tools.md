# MCP-style diagnostic tools (PR #11)

`DiagnosticToolRegistry` introduces a structured, read-only tool layer for AI agents. Each tool has a name, description and JSON input schema.

| Tool | Arguments | Behavior |
| --- | --- | --- |
| `list_incidents` | `{}` | Returns current incident list |
| `get_incident` | `{"incidentId":"..."}` | Returns incident and audit history |
| `diagnose_incident` | `{"incidentId":"..."}` | Returns offline advisory diagnostic assessment |

Example:

```csharp
var tools = new DiagnosticToolRegistry(store, diagnosticService);
var result = await tools.InvokeAsync(new DiagnosticToolCall(
    "get_incident", """{"incidentId":"incident-1"}"""));
```

This is a **local MCP-style tool registry**, not a fully protocol-compliant MCP server. It has no stdio/SSE/Streamable HTTP transport, JSON-RPC negotiation, authentication, or external agent connectivity. Those require a separate transport and security milestone.

Tool invocation never changes incident state or executes recovery actions. The diagnostic provider is the offline mock from PR #10. No cloud calls or paid resources are required.
