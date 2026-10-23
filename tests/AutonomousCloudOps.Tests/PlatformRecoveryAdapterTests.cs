using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class PlatformRecoveryAdapterTests
{
    private static RecoveryAction Action(RecoveryActionKind kind, int? replicas = null) =>
        new("action-1", "incident-1", "service-a", kind, "Hypothetical mitigation", replicas);

    private static RecoveryTarget Aks() =>
        new(RecoveryPlatform.Aks, "sub-1", "rg-demo", "service-a", "production", "frontend");

    private static RecoveryTarget ContainerApps() =>
        new(RecoveryPlatform.ContainerApps, "sub-1", "rg-demo", "service-a");

    [Theory]
    [InlineData(RecoveryActionKind.RestartService, null)]
    [InlineData(RecoveryActionKind.ScaleOut, 3)]
    [InlineData(RecoveryActionKind.InvestigateOnly, null)]
    public void AksPreviewNeverExecutes(RecoveryActionKind kind, int? replicas)
    {
        var preview = new AksRecoveryPreviewAdapter().Preview(Action(kind, replicas), Aks());
        Assert.False(preview.Executed);
        Assert.Equal(RecoveryPlatform.Aks, preview.Platform);
        Assert.Contains("frontend", preview.ProposedOperation);
        Assert.NotEmpty(preview.Preconditions);
    }

    [Theory]
    [InlineData(RecoveryActionKind.RestartService, null)]
    [InlineData(RecoveryActionKind.ScaleOut, 3)]
    [InlineData(RecoveryActionKind.InvestigateOnly, null)]
    public void ContainerAppsPreviewNeverExecutes(RecoveryActionKind kind, int? replicas)
    {
        var preview = new ContainerAppsRecoveryPreviewAdapter().Preview(Action(kind, replicas), ContainerApps());
        Assert.False(preview.Executed);
        Assert.Contains("service-a", preview.ProposedOperation);
        Assert.NotEmpty(preview.Warnings);
    }

    [Fact]
    public void RejectsMismatchedResource()
    {
        var target = Aks() with { ResourceName = "other" };
        Assert.Throws<ArgumentException>(() =>
            new AksRecoveryPreviewAdapter().Preview(Action(RecoveryActionKind.RestartService), target));
    }

    [Fact]
    public void RejectsMissingAksNamespace()
    {
        var target = Aks() with { Namespace = null };
        Assert.Throws<ArgumentException>(() =>
            new AksRecoveryPreviewAdapter().Preview(Action(RecoveryActionKind.RestartService), target));
    }

    [Fact]
    public void RejectsKubernetesFieldsForContainerApps()
    {
        var target = ContainerApps() with { Namespace = "default" };
        Assert.Throws<ArgumentException>(() =>
            new ContainerAppsRecoveryPreviewAdapter().Preview(Action(RecoveryActionKind.RestartService), target));
    }

    [Fact]
    public void RejectsUnsafeScaleTarget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContainerAppsRecoveryPreviewAdapter().Preview(Action(RecoveryActionKind.ScaleOut, 99), ContainerApps()));
    }

    [Fact]
    public void DispatcherRoutesByPlatform()
    {
        var service = new PlatformRecoveryPreviewService(
        [
            new AksRecoveryPreviewAdapter(), new ContainerAppsRecoveryPreviewAdapter()
        ]);
        var preview = service.Preview(Action(RecoveryActionKind.ScaleOut, 2), Aks());
        Assert.Equal(RecoveryPlatform.Aks, preview.Platform);
        Assert.False(preview.Executed);
    }

    [Fact]
    public void DispatcherRejectsUnsupportedPlatform()
    {
        var service = new PlatformRecoveryPreviewService([new AksRecoveryPreviewAdapter()]);
        Assert.Throws<NotSupportedException>(() =>
            service.Preview(Action(RecoveryActionKind.RestartService), ContainerApps()));
    }
}
