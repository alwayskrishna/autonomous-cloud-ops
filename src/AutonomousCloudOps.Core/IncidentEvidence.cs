namespace AutonomousCloudOps.Core;

public enum EvidenceKind { Metric, Log, Deployment }

/// <summary>Sanitized operational observation. Never store secrets or raw personal data.</summary>
public sealed record EvidenceRecord(
    string Id,
    string ResourceId,
    DateTimeOffset Timestamp,
    EvidenceKind Kind,
    string Summary);

public sealed record IncidentEvidenceBundle(
    string IncidentId,
    string ResourceId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyList<EvidenceRecord> Records);

public interface IIncidentEvidenceSource
{
    Task<IReadOnlyList<EvidenceRecord>> QueryAsync(
        string resourceId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Deterministic offline evidence reader with bounded, resource-scoped queries.</summary>
public sealed class InMemoryIncidentEvidenceSource : IIncidentEvidenceSource
{
    private readonly IReadOnlyList<EvidenceRecord> _records;

    public InMemoryIncidentEvidenceSource(IEnumerable<EvidenceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = records.ToArray();
        if (_records.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) ||
            string.IsNullOrWhiteSpace(r.ResourceId) || string.IsNullOrWhiteSpace(r.Summary)))
            throw new ArgumentException("Evidence records require ID, resource and summary.", nameof(records));
    }

    public Task<IReadOnlyList<EvidenceRecord>> QueryAsync(
        string resourceId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<EvidenceRecord> matches = _records
            .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal) &&
                r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(matches);
    }
}

/// <summary>Retrieves bounded evidence around an incident's detection time.</summary>
public sealed class IncidentEvidenceService
{
    private readonly IIncidentEvidenceSource _source;
    private readonly TimeSpan _lookback;
    private readonly TimeSpan _lookahead;
    private readonly int _maxRecords;

    public IncidentEvidenceService(IIncidentEvidenceSource source,
        TimeSpan? lookback = null, TimeSpan? lookahead = null, int maxRecords = 100)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _lookback = lookback ?? TimeSpan.FromMinutes(30);
        _lookahead = lookahead ?? TimeSpan.FromMinutes(5);
        if (_lookback < TimeSpan.Zero || _lookahead < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lookback), "Evidence windows must be nonnegative.");
        if (maxRecords is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxRecords), "Record limit must be between 1 and 1000.");
        _maxRecords = maxRecords;
    }

    public async Task<IncidentEvidenceBundle> RetrieveAsync(
        Incident incident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.ResourceId);
        var from = incident.DetectedAt - _lookback;
        var to = incident.DetectedAt + _lookahead;
        using var activity = CloudOpsObservability.ActivitySource.StartActivity("incident.retrieve_evidence");
        var raw = await _source.QueryAsync(incident.ResourceId, from, to, cancellationToken);
        if (raw is null) throw new InvalidDataException("Evidence source returned null.");
        var records = raw
            .Where(r => r is not null &&
                string.Equals(r.ResourceId, incident.ResourceId, StringComparison.Ordinal) &&
                r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Take(_maxRecords).ToArray();
        activity?.SetTag("evidence.count", records.Length);
        return new IncidentEvidenceBundle(incident.Id, incident.ResourceId, from, to, records);
    }
}
