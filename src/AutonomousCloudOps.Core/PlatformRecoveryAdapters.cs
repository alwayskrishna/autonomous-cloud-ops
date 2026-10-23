namespace AutonomousCloudOps.Core;

public enum RecoveryPlatform { Aks, ContainerApps }

/// <summary>Explicit, structured target; never parse arbitrary shell commands from a model.</summary>
public sealed record RecoveryTarget(
    RecoveryPlatform Platform,
    string SubscriptionId,
    string ResourceGroup,
    string ResourceName,
    string? Namespace = null,
    string? WorkloadName = null);

public sealed record PlatformRecoveryPreview(
    RecoveryPlatform Platform,
    string ActionId,
    string ResourceId,
    bool Executed,
    string ProposedOperation,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> Warnings);

public interface IPlatformRecoveryAdapter
{
    RecoveryPlatform Platform { get; }
    PlatformRecoveryPreview Preview(RecoveryAction action, RecoveryTarget target);
}

/// <summary>Routes only explicitly supported platforms; no execution method exists.</summary>
public sealed class PlatformRecoveryPreviewService
{
    private readonly IReadOnlyDictionary<RecoveryPlatform, IPlatformRecoveryAdapter> _adapters;

    public PlatformRecoveryPreviewService(IEnumerable<IPlatformRecoveryAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        var list = adapters.ToArray();
        if (list.Any(a => a is null) || list.Select(a => a.Platform).Distinct().Count() != list.Length)
            throw new ArgumentException("Platform adapters must be non-null and unique.", nameof(adapters));
        _adapters = list.ToDictionary(a => a.Platform);
    }

    public PlatformRecoveryPreview Preview(RecoveryAction action, RecoveryTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_adapters.TryGetValue(target.Platform, out var adapter))
            throw new NotSupportedException("Unsupported recovery platform.");
        return adapter.Preview(action, target);
    }
}

internal static class RecoveryTargetValidator
{
    public static void Validate(RecoveryAction action, RecoveryTarget target, RecoveryPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Platform != platform) throw new ArgumentException("Platform mismatch.", nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(target.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ResourceGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ResourceName);
        if (target.SubscriptionId.Length > 128 || target.ResourceGroup.Length > 128 ||
            target.ResourceName.Length > 128)
            throw new ArgumentException("Target identifiers exceed the maximum length.", nameof(target));
        // Enforce the existing recovery action safety contract.
        new RecoveryDryRunService().Simulate(action);
        // No inference or fuzzy matching: the proposed action must match the selected resource.
        if (!string.Equals(action.ResourceId, target.ResourceName, StringComparison.Ordinal))
            throw new ArgumentException("Recovery action resource does not match target resource.", nameof(target));
    }
}

/// <summary>Offline AKS preview: emits descriptive operations, never kubectl or Kubernetes API calls.</summary>
public sealed class AksRecoveryPreviewAdapter : IPlatformRecoveryAdapter
{
    public RecoveryPlatform Platform => RecoveryPlatform.Aks;

    public PlatformRecoveryPreview Preview(RecoveryAction action, RecoveryTarget target)
    {
        RecoveryTargetValidator.Validate(action, target, Platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.WorkloadName);
        var operation = action.Kind switch
        {
            RecoveryActionKind.RestartService =>
                $"Would request rollout restart of deployment '{target.WorkloadName}' in namespace '{target.Namespace}'.",
            RecoveryActionKind.ScaleOut =>
                $"Would propose {action.TargetInstanceCount} replicas for deployment '{target.WorkloadName}' in namespace '{target.Namespace}'.",
            RecoveryActionKind.InvestigateOnly =>
                $"Would inspect deployment '{target.WorkloadName}' in namespace '{target.Namespace}' without changes.",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        return new PlatformRecoveryPreview(Platform, action.Id, action.ResourceId, false, operation,
            ["Confirm cluster and namespace ownership", "Verify deployment exists and disruption budget"],
            ["Simulation only; no Kubernetes client", "Approval does not trigger execution"]);
    }
}

/// <summary>Offline Azure Container Apps preview: no Azure SDK, credentials or HTTP calls.</summary>
public sealed class ContainerAppsRecoveryPreviewAdapter : IPlatformRecoveryAdapter
{
    public RecoveryPlatform Platform => RecoveryPlatform.ContainerApps;

    public PlatformRecoveryPreview Preview(RecoveryAction action, RecoveryTarget target)
    {
        RecoveryTargetValidator.Validate(action, target, Platform);
        if (target.Namespace is not null || target.WorkloadName is not null)
            throw new ArgumentException("Container Apps targets must not specify Kubernetes namespace/workload.", nameof(target));
        var operation = action.Kind switch
        {
            RecoveryActionKind.RestartService =>
                $"Would evaluate restarting active revision for Container App '{target.ResourceName}'.",
            RecoveryActionKind.ScaleOut =>
                $"Would propose setting a maximum of {action.TargetInstanceCount} replicas for Container App '{target.ResourceName}', subject to scaling policy.",
            RecoveryActionKind.InvestigateOnly =>
                $"Would inspect Container App '{target.ResourceName}' without changes.",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        return new PlatformRecoveryPreview(Platform, action.Id, action.ResourceId, false, operation,
            ["Verify subscription and resource group ownership", "Review revisions, scaling rules and minimum replicas"],
            ["Simulation only; no Azure SDK or API calls", "Scale-out preview does not guarantee additional running replicas"]);
    }
}
