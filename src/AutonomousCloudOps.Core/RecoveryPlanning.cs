namespace AutonomousCloudOps.Core;

public enum RecoveryActionKind { RestartService, ScaleOut, InvestigateOnly }

/// <summary>A proposed action, never an instruction to mutate infrastructure.</summary>
public sealed record RecoveryAction(
    string Id,
    string IncidentId,
    string ResourceId,
    RecoveryActionKind Kind,
    string Rationale,
    int? TargetInstanceCount = null);

public sealed record RecoveryPlan(
    string IncidentId,
    IReadOnlyList<RecoveryAction> Actions,
    bool RequiresHumanApproval = true);

public sealed record RecoveryDryRunResult(
    string ActionId,
    string IncidentId,
    string ResourceId,
    RecoveryActionKind Kind,
    bool Executed,
    string PredictedEffect,
    IReadOnlyList<string> SafetyChecks);

/// <summary>Purely advisory recovery planning. No Azure or infrastructure dependencies.</summary>
public sealed class RecoveryPlanner
{
    public RecoveryPlan Propose(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.ResourceId);

        var reason = incident.Reason?.ToLowerInvariant() ?? "";
        RecoveryActionKind kind;
        string rationale;
        int? instances = null;
        if (reason.Contains("cpu", StringComparison.Ordinal))
        {
            kind = RecoveryActionKind.ScaleOut;
            rationale = "High CPU may indicate compute saturation; verify load and scaling limits first.";
            instances = 2;
        }
        else if (reason.Contains("memory", StringComparison.Ordinal))
        {
            kind = RecoveryActionKind.RestartService;
            rationale = "Memory pressure may require investigation; restart is only a hypothetical mitigation.";
        }
        else
        {
            kind = RecoveryActionKind.InvestigateOnly;
            rationale = "Insufficient evidence for a specific recovery action.";
        }

        return new RecoveryPlan(incident.Id,
        [
            new RecoveryAction(Guid.NewGuid().ToString("N"), incident.Id,
                incident.ResourceId, kind, rationale, instances)
        ]);
    }
}

/// <summary>
/// Validates recovery proposals and returns hypothetical effects.
/// This component deliberately has no execute method or cloud client.
/// </summary>
public sealed class RecoveryDryRunService
{
    public RecoveryDryRunResult Simulate(RecoveryAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.IncidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.ResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Rationale);
        if (!Enum.IsDefined(action.Kind))
            throw new ArgumentOutOfRangeException(nameof(action), "Unknown recovery action kind.");
        if (action.Kind == RecoveryActionKind.ScaleOut && action.TargetInstanceCount is not (>= 2 and <= 10))
            throw new ArgumentOutOfRangeException(nameof(action), "Scale-out target must be between 2 and 10.");
        if (action.Kind != RecoveryActionKind.ScaleOut && action.TargetInstanceCount is not null)
            throw new ArgumentException("Only scale-out actions may specify a target instance count.", nameof(action));

        var effect = action.Kind switch
        {
            RecoveryActionKind.RestartService =>
                "Would request a service restart; availability impact and root cause remain unknown.",
            RecoveryActionKind.ScaleOut =>
                $"Would request scaling to {action.TargetInstanceCount} instances; capacity and cost not evaluated.",
            _ => "Would gather additional evidence without modifying infrastructure."
        };
        return new RecoveryDryRunResult(action.Id, action.IncidentId, action.ResourceId,
            action.Kind, false, effect,
            ["Simulation only: no infrastructure API calls",
             "Human approval required before any future execution",
             "Validate resource ownership, blast radius and rollback before production use"]);
    }
}
