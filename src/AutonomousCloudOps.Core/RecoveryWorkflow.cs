namespace AutonomousCloudOps.Core;

public enum RecoveryWorkflowStatus { AwaitingApproval, Approved, Rejected }

/// <summary>Snapshot of an advisory workflow. No step can mutate cloud resources.</summary>
public sealed record RecoveryWorkflow(
    string Id,
    string IncidentId,
    RecoveryWorkflowStatus Status,
    DiagnosticAssessment Diagnosis,
    IncidentEvidenceBundle Evidence,
    RecoveryApprovalRequest Approval,
    RecoveryDryRunResult Simulation);

/// <summary>
/// Offline orchestration of diagnostics, evidence, planning, approval and simulation.
/// Approval state is re-read on every Get call; no execution capability exists.
/// </summary>
public sealed class RecoveryWorkflowService
{
    private readonly IncidentLifecycleStore _incidents;
    private readonly IncidentDiagnosticService _diagnostics;
    private readonly IncidentEvidenceService _evidence;
    private readonly RecoveryPlanner _planner;
    private readonly RecoveryDryRunService _simulator;
    private readonly RecoveryApprovalStore _approvals;
    private readonly object _gate = new();
    private readonly Dictionary<string, RecoveryWorkflow> _workflows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byIncident = new(StringComparer.Ordinal);

    public RecoveryWorkflowService(IncidentLifecycleStore incidents,
        IncidentDiagnosticService diagnostics, IncidentEvidenceService evidence,
        RecoveryPlanner planner, RecoveryDryRunService simulator, RecoveryApprovalStore approvals)
    {
        _incidents = incidents ?? throw new ArgumentNullException(nameof(incidents));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
    }

    public async Task<RecoveryWorkflow> StartAsync(string incidentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        // Serializes duplicate submissions and ensures only one approval per incident.
        // The gate is not held across asynchronous provider calls.
        lock (_gate)
        {
            if (_byIncident.TryGetValue(incidentId, out var existing))
                return Refresh(_workflows[existing]);
        }

        var incident = _incidents.Get(incidentId).Incident;
        var diagnosis = await _diagnostics.DiagnoseAsync(incident, cancellationToken);
        var evidence = await _evidence.RetrieveAsync(incident, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var action = _planner.Propose(incident).Actions.Single();
        var simulation = _simulator.Simulate(action);

        lock (_gate)
        {
            if (_byIncident.TryGetValue(incidentId, out var existing))
                return Refresh(_workflows[existing]);
            var approval = _approvals.Submit(action, DateTimeOffset.UtcNow);
            var workflow = new RecoveryWorkflow(Guid.NewGuid().ToString("N"), incidentId,
                RecoveryWorkflowStatus.AwaitingApproval, diagnosis, evidence, approval, simulation);
            _workflows.Add(workflow.Id, workflow);
            _byIncident.Add(incidentId, workflow.Id);
            return workflow;
        }
    }

    public RecoveryWorkflow Get(string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        lock (_gate)
        {
            return _workflows.TryGetValue(workflowId, out var workflow)
                ? Refresh(workflow)
                : throw new KeyNotFoundException($"Workflow '{workflowId}' not found.");
        }
    }

    public IReadOnlyList<RecoveryWorkflow> List()
    {
        lock (_gate)
            return _workflows.Values.Select(Refresh).OrderBy(w => w.Id, StringComparer.Ordinal).ToArray();
    }

    private RecoveryWorkflow Refresh(RecoveryWorkflow workflow)
    {
        var approval = _approvals.Get(workflow.Approval.Id);
        var status = approval.Status switch
        {
            RecoveryApprovalStatus.Approved => RecoveryWorkflowStatus.Approved,
            RecoveryApprovalStatus.Rejected => RecoveryWorkflowStatus.Rejected,
            _ => RecoveryWorkflowStatus.AwaitingApproval
        };
        return workflow with { Approval = approval, Status = status };
    }
}
