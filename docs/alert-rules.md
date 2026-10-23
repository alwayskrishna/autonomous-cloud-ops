# Configurable alert rules (PR #4)

`AlertPolicy` accepts validated immutable rule snapshots. Each `AlertRule` defines a unique ID, metric (CPU %, memory %, HTTP 5xx count), inclusive threshold, severity, optional resource pattern and enabled flag.

Patterns support exact resource IDs, `*` (all resources), `prod/*` (prefix) and `*suffix` (suffix). Matching is case-sensitive. Rules are evaluated in configured order, and **every matching rule** creates a separate incident. There is no deduplication or priority override yet (planned for PR #5).

`AlertPolicy.Default` preserves the original 90% CPU, 90% memory, and 10 HTTP 5xx thresholds. This PR adds a separate `AlertRuleEngine` while retaining `IncidentDetector` for backward compatibility.

Example:

```csharp
var policy = new AlertPolicy(new[]
{
    new AlertRule("prod-cpu", TelemetryMetric.CpuPercent, 85, IncidentSeverity.Critical, "prod/*"),
    new AlertRule("all-errors", TelemetryMetric.Http5xxCount, 5, IncidentSeverity.Warning)
});
var incidents = new AlertRuleEngine(policy).Evaluate(sample);
```

Rules are currently supplied in code, not fetched from a remote configuration service. No Azure resources or recovery operations are required.
