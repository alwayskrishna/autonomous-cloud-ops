using System.Text.Json;
using AutonomousCloudOps.Core;

// Minimal local MCP JSON-RPC 2.0 stdio transport. Never write diagnostics to stdout.
var store = new IncidentLifecycleStore();
var diagnostics = new IncidentDiagnosticService(new MockIncidentDiagnosticProvider());
var tools = new DiagnosticToolRegistry(store, diagnostics);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
while (await Console.In.ReadLineAsync() is { } line)
{
    try
    {
        using var message = JsonDocument.Parse(line);
        var root = message.RootElement;
        if (!root.TryGetProperty("method", out var methodElement)) continue;
        var method = methodElement.GetString();
        // Notifications do not receive JSON-RPC responses.
        if (!root.TryGetProperty("id", out var id)) continue;
        var idValue = id.Clone();
        object response;
        if (method == "initialize")
        {
            response = new
            {
                jsonrpc = "2.0", id = idValue,
                result = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "autonomous-cloud-ops", version = "0.1.0" }
                }
            };
        }
        else if (method == "ping")
            response = new { jsonrpc = "2.0", id = idValue, result = new { } };
        else if (method == "tools/list")
            response = new
            {
                jsonrpc = "2.0", id = idValue,
                result = new
                {
                    tools = tools.ListTools().Select(t => new
                    {
                        name = t.Name, description = t.Description,
                        inputSchema = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJson)
                    }).ToArray()
                }
            };
        else if (method == "tools/call")
        {
            var parameters = root.GetProperty("params");
            var name = parameters.GetProperty("name").GetString() ?? "";
            var arguments = parameters.TryGetProperty("arguments", out var args)
                ? args.GetRawText() : "{}";
            var result = await tools.InvokeAsync(new DiagnosticToolCall(name, arguments));
            response = new
            {
                jsonrpc = "2.0", id = idValue,
                result = new
                {
                    content = new[] { new { type = "text", text = result.ContentJson } },
                    isError = result.IsError
                }
            };
        }
        else
            response = new { jsonrpc = "2.0", id = idValue,
                error = new { code = -32601, message = "Method not found" } };
        Console.WriteLine(JsonSerializer.Serialize(response, json));
    }
    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
    {
        // Avoid echoing potentially sensitive client payloads to stderr or stdout.
        Console.Error.WriteLine("Malformed MCP JSON-RPC request ignored.");
    }
}
