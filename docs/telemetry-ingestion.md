# Telemetry ingestion (PR #2)

`ITelemetrySource` is an asynchronous, cancellation-aware provider contract. `SimulatedTelemetrySource` replays an in-memory sequence of `TelemetrySample` objects without network access. `TelemetryIngestionService` reads up to a caller-defined `maxSamples` limit (default 10,000), applies `IncidentDetector` to each sample, and returns the number processed plus detected incidents.

## Example

```csharp
var source = new SimulatedTelemetrySource(new[]
{
    new TelemetrySample("demo/node-1", DateTimeOffset.UtcNow, 95, 40, 0),
    new TelemetrySample("demo/node-2", DateTimeOffset.UtcNow, 20, 30, 0)
});
var result = await new TelemetryIngestionService(new IncidentDetector()).IngestAsync(source);
Console.WriteLine($"Processed {result.SamplesProcessed}; incidents {result.Incidents.Count}");
```

The limit bounds sample consumption, **not** incident count or memory usage of an arbitrary sample provider. Sources must cooperate with cancellation. The service fails fast on invalid samples; retries, checkpoints, telemetry validation policies and persistent storage are future milestones. No cloud credentials or billable Azure services are needed.
