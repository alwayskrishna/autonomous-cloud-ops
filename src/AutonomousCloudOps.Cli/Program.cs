using System.Text.Json;
using AutonomousCloudOps.Core;

var now = DateTimeOffset.UtcNow;
var source = new SimulatedTelemetrySource(new[]
{
    new TelemetrySample("demo/container-app-01", now, 95, 62, 12),
    new TelemetrySample("demo/container-app-02", now, 34, 48, 0),
    new TelemetrySample("demo/container-app-03", now, 40, 93, 0)
});
var result = await new TelemetryIngestionService(new IncidentDetector()).IngestAsync(source);
var store = new IncidentLifecycleStore();
foreach (var incident in result.Incidents)
    store.Open(incident);

if (result.Incidents.Count > 0)
{
    var incident = result.Incidents[0];
    store.Acknowledge(incident.Id, "demo-operator", now.AddMinutes(1), "Investigating");
    store.Resolve(incident.Id, "demo-operator", now.AddMinutes(2), "Simulation complete");
}

Console.WriteLine(JsonSerializer.Serialize(
    new { result.SamplesProcessed, Incidents = store.List() },
    new JsonSerializerOptions { WriteIndented = true }));
