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

/// <summary>Compatibility facade over the configurable rule engine.</summary>
public sealed class IncidentDetector
{
    private readonly AlertRuleEngine _engine;

    public IncidentDetector() : this(AlertPolicy.Default) { }

    public IncidentDetector(AlertPolicy policy) =>
        _engine = new AlertRuleEngine(policy);

    public IReadOnlyList<Incident> Detect(TelemetrySample sample) => _engine.Evaluate(sample);
}
