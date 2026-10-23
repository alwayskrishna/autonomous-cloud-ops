# Root-cause evidence retrieval (PR #12)

The evidence layer introduces `IIncidentEvidenceSource`, `IncidentEvidenceService`, and `IncidentEvidenceBundle`. It supports **metric**, **log** and **deployment** observations in a resource-scoped, bounded time window around an incident.

Default retrieval includes the 30 minutes before detection and 5 minutes afterward, with a maximum of 100 records. Results are deterministically ordered by timestamp and record ID.

```csharp
var evidence = new IncidentEvidenceService(new InMemoryIncidentEvidenceSource(new[]
{
    new EvidenceRecord("metric-1", incident.ResourceId, incident.DetectedAt,
        EvidenceKind.Metric, "CPU 96%")
}));
var bundle = await evidence.RetrieveAsync(incident);
```

The local MCP-style tool registry exposes `get_incident_evidence` with an `incidentId` argument. This is **read-only**, and the source defaults to an empty in-memory reader until evidence is supplied.

**Limitations:** No live Azure Monitor, Log Analytics or deployment-history queries are performed. Evidence summaries must be sanitized before ingestion; no automatic redaction is provided. These observations help investigate potential causes but do not establish causality on their own.
