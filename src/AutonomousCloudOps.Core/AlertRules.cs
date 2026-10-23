namespace AutonomousCloudOps.Core;

public enum TelemetryMetric { CpuPercent, MemoryPercent, Http5xxCount }

public sealed record AlertRule(
    string Id,
    TelemetryMetric Metric,
    double Threshold,
    IncidentSeverity Severity,
    string ResourcePattern = "*",
    bool Enabled = true);

/// <summary>Validated immutable policy snapshot; '*' means all resources or a prefix/suffix wildcard.</summary>
public sealed class AlertPolicy
{
    public IReadOnlyList<AlertRule> Rules { get; }

    public AlertPolicy(IEnumerable<AlertRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var snapshot = rules.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in snapshot)
        {
            if (rule is null)
                throw new ArgumentException("Rules cannot be null.", nameof(rules));
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
                throw new ArgumentException("Rule IDs must be nonempty and unique.", nameof(rules));
            if (!Enum.IsDefined(rule.Metric) || !Enum.IsDefined(rule.Severity))
                throw new ArgumentException("Unknown metric or severity.", nameof(rules));
            if (!double.IsFinite(rule.Threshold) || rule.Threshold < 0 ||
                (rule.Metric != TelemetryMetric.Http5xxCount && rule.Threshold > 100) ||
                (rule.Metric == TelemetryMetric.Http5xxCount && rule.Threshold != Math.Truncate(rule.Threshold)))
                throw new ArgumentException("Invalid rule threshold.", nameof(rules));
            if (string.IsNullOrWhiteSpace(rule.ResourcePattern) ||
                (rule.ResourcePattern.Contains('*') &&
                 (rule.ResourcePattern.Count(c => c == '*') != 1 ||
                  (rule.ResourcePattern[0] != '*' && rule.ResourcePattern[^1] != '*'))))
                throw new ArgumentException("Resource patterns support only one leading or trailing wildcard.", nameof(rules));
        }
        Rules = Array.AsReadOnly(snapshot);
    }

    public static AlertPolicy Default { get; } = new(new[]
    {
        new AlertRule("cpu-high", TelemetryMetric.CpuPercent, 90, IncidentSeverity.Critical),
        new AlertRule("memory-high", TelemetryMetric.MemoryPercent, 90, IncidentSeverity.Critical),
        new AlertRule("http-errors", TelemetryMetric.Http5xxCount, 10, IncidentSeverity.Warning)
    });
}

public sealed class AlertRuleEngine
{
    private readonly AlertPolicy _policy;

    public AlertRuleEngine(AlertPolicy policy) => _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public IReadOnlyList<Incident> Evaluate(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (string.IsNullOrWhiteSpace(sample.ResourceId))
            throw new ArgumentException("ResourceId is required.", nameof(sample));
        if (!double.IsFinite(sample.CpuPercent) || sample.CpuPercent is < 0 or > 100 ||
            !double.IsFinite(sample.MemoryPercent) || sample.MemoryPercent is < 0 or > 100 ||
            sample.Http5xxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sample), "Invalid telemetry values.");

        var incidents = new List<Incident>();
        foreach (var rule in _policy.Rules)
        {
            if (!rule.Enabled || !Matches(rule.ResourcePattern, sample.ResourceId))
                continue;
            var value = rule.Metric switch
            {
                TelemetryMetric.CpuPercent => sample.CpuPercent,
                TelemetryMetric.MemoryPercent => sample.MemoryPercent,
                TelemetryMetric.Http5xxCount => sample.Http5xxCount,
                _ => throw new InvalidOperationException("Unsupported metric.")
            };
            if (value >= rule.Threshold)
                incidents.Add(new Incident(Guid.NewGuid().ToString("N"), sample.ResourceId, sample.Timestamp,
                    rule.Severity, $"Rule {rule.Id}: {rule.Metric} {value} >= {rule.Threshold}"));
        }
        return incidents;
    }

    private static bool Matches(string pattern, string resource) =>
        pattern == "*" ||
        (pattern.EndsWith('*') && resource.StartsWith(pattern[..^1], StringComparison.Ordinal)) ||
        (pattern.StartsWith('*') && resource.EndsWith(pattern[1..], StringComparison.Ordinal)) ||
        string.Equals(pattern, resource, StringComparison.Ordinal);
}
