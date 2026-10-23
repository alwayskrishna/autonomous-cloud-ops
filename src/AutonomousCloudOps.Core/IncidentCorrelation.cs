namespace AutonomousCloudOps.Core;

/// <summary>A snapshot of a group of repeated alerts from the same resource and rule.</summary>
public sealed record CorrelatedIncident(
    string Id,
    string ResourceId,
    string CorrelationKey,
    IncidentSeverity Severity,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int OccurrenceCount,
    string LatestReason);

/// <summary>
/// Process-local, thread-safe alert deduplication with a configurable inactivity window.
/// Incidents are grouped by resource ID and rule ID, not by changing metric values.
/// </summary>
public sealed class IncidentCorrelator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CorrelatedIncident> _active = new(StringComparer.Ordinal);
    private readonly TimeSpan _window;

    public IncidentCorrelator(TimeSpan? inactivityWindow = null)
    {
        _window = inactivityWindow ?? TimeSpan.FromMinutes(5);
        if (_window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(inactivityWindow));
    }

    public CorrelatedIncident Add(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (string.IsNullOrWhiteSpace(incident.Id) ||
            string.IsNullOrWhiteSpace(incident.ResourceId) ||
            string.IsNullOrWhiteSpace(incident.Reason))
            throw new ArgumentException("Incident ID, resource, and reason are required.", nameof(incident));

        var key = BuildKey(incident);
        lock (_gate)
        {
            if (_active.TryGetValue(key, out var previous))
            {
                if (incident.DetectedAt < previous.LastSeen)
                    throw new ArgumentOutOfRangeException(nameof(incident),
                        "Events for the same correlation key must arrive in timestamp order.");

                if (incident.DetectedAt - previous.LastSeen <= _window)
                {
                    var updated = previous with
                    {
                        LastSeen = incident.DetectedAt,
                        OccurrenceCount = checked(previous.OccurrenceCount + 1),
                        Severity = MaxSeverity(previous.Severity, incident.Severity),
                        LatestReason = incident.Reason
                    };
                    _active[key] = updated;
                    return updated;
                }
            }

            var created = new CorrelatedIncident(
                incident.Id, incident.ResourceId, key, incident.Severity,
                incident.DetectedAt, incident.DetectedAt, 1, incident.Reason);
            _active[key] = created;
            return created;
        }
    }

    public IReadOnlyList<CorrelatedIncident> Snapshot()
    {
        lock (_gate)
        {
            return _active.Values
                .OrderBy(i => i.ResourceId, StringComparer.Ordinal)
                .ThenBy(i => i.CorrelationKey, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private static IncidentSeverity MaxSeverity(IncidentSeverity left, IncidentSeverity right) =>
        left == IncidentSeverity.Critical || right == IncidentSeverity.Critical
            ? IncidentSeverity.Critical : IncidentSeverity.Warning;

    private static string BuildKey(Incident incident)
    {
        // AlertRuleEngine formats reasons as "Rule {id}: {metric} {value} >= {threshold}".
        // Extracting the rule ID avoids creating a new group every time the metric value changes.
        const string prefix = "Rule ";
        var reason = incident.Reason;
        var ruleId = reason.StartsWith(prefix, StringComparison.Ordinal)
            ? reason[prefix.Length..].Split(':', 2)[0].Trim()
            : reason;
        if (ruleId.Length == 0)
            ruleId = reason;
        return $"{incident.ResourceId.Length}:{incident.ResourceId}{ruleId.Length}:{ruleId}";
    }
}
