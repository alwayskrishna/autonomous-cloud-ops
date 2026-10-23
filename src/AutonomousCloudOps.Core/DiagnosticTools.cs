using System.Text.Json;

namespace AutonomousCloudOps.Core;

/// <summary>Metadata for discoverable, read-only diagnostic tools.</summary>
public sealed record DiagnosticToolDefinition(string Name, string Description, string InputSchemaJson);

public sealed record DiagnosticToolCall(string Name, string ArgumentsJson);

public sealed record DiagnosticToolResult(string Name, string ContentJson, bool IsError = false);

/// <summary>
/// Local MCP-style tool dispatcher. It does not implement the MCP wire protocol or expose a network listener.
/// All tools are read-only; no lifecycle mutations or remediation actions are available.
/// </summary>
public sealed class DiagnosticToolRegistry
{
    private readonly IncidentLifecycleStore _store;
    private readonly IncidentDiagnosticService _diagnostics;
    private readonly IncidentEvidenceService _evidence;

    private static readonly IReadOnlyList<DiagnosticToolDefinition> Definitions =
    [
        new("list_incidents", "List known incidents and their current states.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("get_incident", "Get one incident including its audit history.",
            """{"type":"object","properties":{"incidentId":{"type":"string"}},"required":["incidentId"],"additionalProperties":false}"""),
        new("diagnose_incident", "Retrieve advisory, offline diagnostic guidance for one incident.",
            """{"type":"object","properties":{"incidentId":{"type":"string"}},"required":["incidentId"],"additionalProperties":false}"""),
        new("get_incident_evidence", "Retrieve bounded metric, log and deployment evidence for one incident.",
            """{"type":"object","properties":{"incidentId":{"type":"string"}},"required":["incidentId"],"additionalProperties":false}""")
    ];

    public DiagnosticToolRegistry(IncidentLifecycleStore store, IncidentDiagnosticService diagnostics,
        IncidentEvidenceService? evidence = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _evidence = evidence ?? new IncidentEvidenceService(new InMemoryIncidentEvidenceSource([]));
    }

    public IReadOnlyList<DiagnosticToolDefinition> ListTools() => Definitions;

    public async Task<DiagnosticToolResult> InvokeAsync(
        DiagnosticToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();

        if (call.Name is not ("list_incidents" or "get_incident" or "diagnose_incident" or "get_incident_evidence"))
            return new DiagnosticToolResult(call.Name, """{"error":"Unknown tool"}""", true);

        try
        {
            using var args = JsonDocument.Parse(call.ArgumentsJson);
            if (args.RootElement.ValueKind != JsonValueKind.Object)
                return Error(call.Name, "Arguments must be a JSON object.");

            var properties = args.RootElement.EnumerateObject().ToArray();
            if (call.Name == "list_incidents")
            {
                if (properties.Length != 0)
                    return Error(call.Name, "list_incidents accepts no arguments.");
                return new DiagnosticToolResult(call.Name, JsonSerializer.Serialize(_store.List()));
            }

            if (properties.Length != 1 || properties[0].Name != "incidentId" ||
                properties[0].Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(properties[0].Value.GetString()))
                return Error(call.Name, "A nonempty incidentId string is required.");

            var id = properties[0].Value.GetString()!;
            var incident = _store.Get(id);
            if (call.Name == "get_incident")
                return new DiagnosticToolResult(call.Name, JsonSerializer.Serialize(incident));

            if (call.Name == "get_incident_evidence")
            {
                var evidence = await _evidence.RetrieveAsync(incident.Incident, cancellationToken);
                return new DiagnosticToolResult(call.Name, JsonSerializer.Serialize(evidence));
            }

            var assessment = await _diagnostics.DiagnoseAsync(incident.Incident, cancellationToken);
            return new DiagnosticToolResult(call.Name, JsonSerializer.Serialize(assessment));
        }
        catch (JsonException)
        {
            return Error(call.Name, "Invalid JSON arguments.");
        }
        catch (KeyNotFoundException)
        {
            return Error(call.Name, "Incident not found.");
        }
    }

    private static DiagnosticToolResult Error(string name, string message) =>
        new(name, JsonSerializer.Serialize(new { error = message }), true);
}
