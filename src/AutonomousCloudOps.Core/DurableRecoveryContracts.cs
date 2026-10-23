namespace AutonomousCloudOps.Core;

/// <summary>Durable orchestration state contract for a future Azure Functions host.</summary>
public enum DurableRecoveryStage { Created, Diagnosing, AwaitingApproval, Approved, Rejected, Completed }

public sealed record DurableRecoveryCheckpoint(
    string WorkflowId, string IncidentId, DurableRecoveryStage Stage,
    DateTimeOffset UpdatedAt, string? ApprovalId = null);

public interface IRecoveryCheckpointStore
{
    Task<DurableRecoveryCheckpoint?> ReadAsync(string workflowId, CancellationToken ct = default);
    Task WriteAsync(DurableRecoveryCheckpoint checkpoint, CancellationToken ct = default);
}

/// <summary>Deterministic state transitions; does not execute cloud operations.</summary>
public sealed class RecoveryOrchestrationStateMachine
{
    public DurableRecoveryCheckpoint Advance(DurableRecoveryCheckpoint current,
        DurableRecoveryStage next, DateTimeOffset at, string? approvalId = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (at < current.UpdatedAt) throw new ArgumentOutOfRangeException(nameof(at));
        var allowed = (current.Stage, next) switch
        {
            (DurableRecoveryStage.Created, DurableRecoveryStage.Diagnosing) => true,
            (DurableRecoveryStage.Diagnosing, DurableRecoveryStage.AwaitingApproval) => true,
            (DurableRecoveryStage.AwaitingApproval, DurableRecoveryStage.Approved) => true,
            (DurableRecoveryStage.AwaitingApproval, DurableRecoveryStage.Rejected) => true,
            (DurableRecoveryStage.Approved, DurableRecoveryStage.Completed) => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException("Invalid orchestration transition.");
        if (next is DurableRecoveryStage.Approved or DurableRecoveryStage.Rejected &&
            string.IsNullOrWhiteSpace(approvalId ?? current.ApprovalId))
            throw new InvalidOperationException("Approval decision requires an approval reference.");
        return current with { Stage = next, UpdatedAt = at, ApprovalId = approvalId ?? current.ApprovalId };
    }
}

public sealed class InMemoryRecoveryCheckpointStore : IRecoveryCheckpointStore
{
    private readonly Dictionary<string, DurableRecoveryCheckpoint> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public Task<DurableRecoveryCheckpoint?> ReadAsync(string workflowId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_states.GetValueOrDefault(workflowId));
    }
    public Task WriteAsync(DurableRecoveryCheckpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ct.ThrowIfCancellationRequested();
        lock (_gate) _states[checkpoint.WorkflowId] = checkpoint;
        return Task.CompletedTask;
    }
}
