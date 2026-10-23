using System.Text.Json;
using AutonomousCloudOps.Core;

var sample = new TelemetrySample(
    ResourceId: "demo/container-app-01",
    Timestamp: DateTimeOffset.UtcNow,
    CpuPercent: 95,
    MemoryPercent: 62,
    Http5xxCount: 12);

var incidents = new IncidentDetector().Detect(sample);
Console.WriteLine(JsonSerializer.Serialize(new { sample, incidents }, new JsonSerializerOptions { WriteIndented = true }));
