namespace AutonomousCloudOps.Core;

public enum IncidentSeverity { Warning, Critical }

public sealed record TelemetrySample(
    string ResourceId,
    DateTimeOffset Timestamp,
    double CpuPercent,
    double MemoryPercent,
    int Http5xxCount);

public sealed record Incident(
    string Id,
    string ResourceId,
    DateTimeOffset DetectedAt,
    IncidentSeverity Severity,
    string Reason);

public sealed class IncidentDetector
{
    public IReadOnlyList<Incident> Detect(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (string.IsNullOrWhiteSpace(sample.ResourceId))
            throw new ArgumentException("ResourceId is required.", nameof(sample));
        if (!double.IsFinite(sample.CpuPercent) || sample.CpuPercent < 0 || sample.CpuPercent > 100 ||
            !double.IsFinite(sample.MemoryPercent) || sample.MemoryPercent < 0 || sample.MemoryPercent > 100 ||
            sample.Http5xxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sample), "Invalid telemetry values.");

        var incidents = new List<Incident>();
        if (sample.CpuPercent >= 90)
            incidents.Add(Create(sample, IncidentSeverity.Critical, "CPU utilization >= 90%"));
        if (sample.MemoryPercent >= 90)
            incidents.Add(Create(sample, IncidentSeverity.Critical, "Memory utilization >= 90%"));
        if (sample.Http5xxCount >= 10)
            incidents.Add(Create(sample, IncidentSeverity.Warning, "HTTP 5xx count >= 10"));
        return incidents;
    }

    private static Incident Create(TelemetrySample sample, IncidentSeverity severity, string reason) =>
        new(Guid.NewGuid().ToString("N"), sample.ResourceId, sample.Timestamp, severity, reason);
}
