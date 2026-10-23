# Azure Monitor-compatible telemetry adapter (PR #9)

This PR introduces a provider-neutral `IAzureMetricReader` boundary and a deterministic `MockAzureMetricReader`. `AzureMonitorTelemetrySource` translates metric datapoints into the platform's `ITelemetrySource` stream.

Supported metric names (case-insensitive):
- `Percentage CPU` → `CpuPercent`
- `Memory Percentage` → `MemoryPercent`
- `Http 5xx` → `Http5xxCount`

The adapter combines consecutive points sharing the same resource ID and timestamp. Missing metric values default to zero, and duplicate metric names within a bucket are rejected. Input must be grouped by resource/timestamp; it does not perform global sorting or merge nonconsecutive buckets. Percentage values must be between 0 and 100, and HTTP error counts must be whole numbers.

```csharp
var source = new AzureMonitorTelemetrySource(new MockAzureMetricReader(new[]
{
    new AzureMetricPoint("azure/vm-01", "Percentage CPU", DateTimeOffset.UtcNow, 96)
}));
var result = await new TelemetryIngestionService(new IncidentDetector()).IngestAsync(source);
```

**No real Azure Monitor API calls are made.** A production reader using Azure identity, resource-scoped permissions, pagination, retries, and Azure Monitor SDK APIs is future work. Metric naming and aggregation differ across Azure resource providers and must be mapped explicitly before live integration. No credentials, billable queries or cloud resources are used.
