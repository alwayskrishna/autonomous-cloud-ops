using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class RecoveryWorkflowTests
{
    private static (RecoveryWorkflowService Service, RecoveryApprovalStore Approvals, Incident Incident) Setup()
    {
        var incident = new Incident("incident-1", "node-a",
            DateTimeOffset.Parse("2026-01-01T12:00:00Z"),
            IncidentSeverity.Critical, "Rule cpu-high: CpuPercent 96 >= 90");
        var incidents = new IncidentLifecycleStore();
        incidents.Open(incident);
        var approvals = new RecoveryApprovalStore();
        var evidence = new IncidentEvidenceService(new InMemoryIncidentEvidenceSource(
        [
            new EvidenceRecord("metric-1", "node-a", incident.DetectedAt, EvidenceKind.Metric, "CPU 96%"),
            new EvidenceRecord("unrelated", "node-b", incident.DetectedAt, EvidenceKind.Log, "Other")
        ]));
        var service = new RecoveryWorkflowService(incidents,
            new IncidentDiagnosticService(new MockIncidentDiagnosticProvider()),
            evidence, new RecoveryPlanner(), new RecoveryDryRunService(), approvals);
        return (service, approvals, incident);
    }

    [Fact]
    public async Task WorkflowCombinesDiagnosisEvidenceAndDryRun()
    {
        var (service, _, incident) = Setup();
        var workflow = await service.StartAsync(incident.Id);
        Assert.Equal(RecoveryWorkflowStatus.AwaitingApproval, workflow.Status);
        Assert.Equal("offline-mock", workflow.Diagnosis.Provider);
        Assert.Equal("metric-1", Assert.Single(workflow.Evidence.Records).Id);
        Assert.False(workflow.Simulation.Executed);
        Assert.Equal(RecoveryApprovalStatus.Pending, workflow.Approval.Status);
    }

    [Fact]
    public async Task ApprovedWorkflowNeverExecutesRecovery()
    {
        var (service, approvals, incident) = Setup();
        var started = await service.StartAsync(incident.Id);
        approvals.Approve(started.Approval.Id, "reviewer",
            started.Approval.RequestedAt.AddSeconds(1), "Reviewed blast radius");
        var updated = service.Get(started.Id);
        Assert.Equal(RecoveryWorkflowStatus.Approved, updated.Status);
        Assert.False(updated.Simulation.Executed);
        Assert.Single(updated.Approval.Decisions);
    }

    [Fact]
    public async Task RejectedWorkflowIsTerminal()
    {
        var (service, approvals, incident) = Setup();
        var started = await service.StartAsync(incident.Id);
        approvals.Reject(started.Approval.Id, "reviewer",
            started.Approval.RequestedAt.AddSeconds(1), "Insufficient evidence");
        Assert.Equal(RecoveryWorkflowStatus.Rejected, service.Get(started.Id).Status);
        Assert.False(service.Get(started.Id).Simulation.Executed);
    }

    [Fact]
    public async Task RepeatedStartIsIdempotent()
    {
        var (service, approvals, incident) = Setup();
        var first = await service.StartAsync(incident.Id);
        var second = await service.StartAsync(incident.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(approvals.List());
        Assert.Single(service.List());
    }

    [Fact]
    public async Task MissingIncidentAndWorkflowAreRejected()
    {
        var (service, _, _) = Setup();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.StartAsync("missing"));
        Assert.Throws<KeyNotFoundException>(() => service.Get("missing"));
    }

    [Fact]
    public async Task CancellationDoesNotCreateApproval()
    {
        var (service, approvals, incident) = Setup();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.StartAsync(incident.Id, cts.Token));
        Assert.Empty(approvals.List());
    }
}
