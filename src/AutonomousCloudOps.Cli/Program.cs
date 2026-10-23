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
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
