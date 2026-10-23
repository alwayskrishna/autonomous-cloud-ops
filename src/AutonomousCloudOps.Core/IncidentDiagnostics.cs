using System.Diagnostics;
using System.Text.Json;

namespace AutonomousCloudOps.Core;

/// <summary>Evidence supplied to a diagnostic provider. Never include credentials or customer data.</summary>
public sealed record DiagnosticRequest(
    string IncidentId,
    string ResourceId,
    IncidentSeverity Severity,
    string Reason,
    DateTimeOffset DetectedAt);

public sealed record DiagnosticAssessment(
    string IncidentId,
    string Summary,
    IReadOnlyList<string> PossibleCauses,
    IReadOnlyList<string> InvestigationSteps,
    double Confidence,
    string Provider);

/// <summary>Replaceable diagnostic reasoning boundary for mock or future Azure OpenAI implementations.</summary>
public interface IIncidentDiagnosticProvider
{
    Task<DiagnosticAssessment> AnalyzeAsync(DiagnosticRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Offline, deterministic diagnostic responses. Does not invoke a model or network.</summary>
public sealed class MockIncidentDiagnosticProvider : IIncidentDiagnosticProvider
{
    public Task<DiagnosticAssessment> AnalyzeAsync(DiagnosticRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var reason = request.Reason.ToLowerInvariant();
        var isCpu = reason.Contains("cpu", StringComparison.Ordinal);
        var isMemory = reason.Contains("memory", StringComparison.Ordinal);
        var isHttp = reason.Contains("5xx", StringComparison.Ordinal) || reason.Contains("http", StringComparison.Ordinal);

        string summary;
        string[] causes;
        string[] steps;
        if (isCpu)
        {
            summary = "Elevated CPU utilization detected; investigate workload pressure.";
            causes = ["Increased request volume", "CPU-intensive processing", "Insufficient compute capacity"];
            steps = ["Compare CPU with request rate", "Inspect recent deployments", "Check CPU profiles and scaling events"];
        }
        else if (isMemory)
        {
            summary = "Elevated memory utilization detected; investigate allocation and capacity.";
            causes = ["Memory-intensive workload", "Unexpected retention or leak", "Insufficient memory allocation"];
            steps = ["Inspect memory trend and garbage collection", "Compare memory with traffic", "Review recent deployments"];
        }
        else if (isHttp)
        {
            summary = "Elevated server error count detected; investigate request failures.";
            causes = ["Upstream dependency failures", "Application exceptions", "Service overload"];
            steps = ["Inspect error logs and traces", "Check downstream dependencies", "Compare error rate with recent deployments"];
        }
        else
        {
            summary = "Incident requires additional diagnostic evidence.";
            causes = ["Unknown operational anomaly"];
            steps = ["Review relevant logs, metrics and traces", "Check recent changes"];
        }

        return Task.FromResult(new DiagnosticAssessment(
            request.IncidentId, summary, causes, steps, 0.0, "offline-mock"));
    }
}

/// <summary>Validates requests and provider responses before returning untrusted diagnostic guidance.</summary>
public sealed class IncidentDiagnosticService
{
    private readonly IIncidentDiagnosticProvider _provider;

    public IncidentDiagnosticService(IIncidentDiagnosticProvider provider) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public async Task<DiagnosticAssessment> DiagnoseAsync(
        Incident incident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (string.IsNullOrWhiteSpace(incident.Id) ||
            string.IsNullOrWhiteSpace(incident.ResourceId) ||
            string.IsNullOrWhiteSpace(incident.Reason))
            throw new ArgumentException("Incident ID, resource and reason are required.", nameof(incident));

        using var activity = CloudOpsObservability.ActivitySource.StartActivity("incident.diagnose");
        var request = new DiagnosticRequest(incident.Id, incident.ResourceId, incident.Severity,
            incident.Reason, incident.DetectedAt);
        var assessment = await _provider.AnalyzeAsync(request, cancellationToken);
        if (assessment is null ||
            assessment.IncidentId != incident.Id ||
            string.IsNullOrWhiteSpace(assessment.Summary) ||
            string.IsNullOrWhiteSpace(assessment.Provider) ||
            assessment.PossibleCauses is null ||
            assessment.InvestigationSteps is null ||
            assessment.PossibleCauses.Any(string.IsNullOrWhiteSpace) ||
            assessment.InvestigationSteps.Any(string.IsNullOrWhiteSpace) ||
            !double.IsFinite(assessment.Confidence) ||
            assessment.Confidence is < 0 or > 1)
            throw new InvalidDataException("Diagnostic provider returned an invalid assessment.");

        activity?.SetTag("diagnostics.provider", assessment.Provider);
        return assessment;
    }
}
