using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class RecoveryPlanningTests
{
    private static Incident Incident(string reason) =>
        new("incident-1", "node-1", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            IncidentSeverity.Critical, reason);

    [Theory]
    [InlineData("CPU 95%", RecoveryActionKind.ScaleOut)]
    [InlineData("Memory 98%", RecoveryActionKind.RestartService)]
    [InlineData("HTTP 5xx errors", RecoveryActionKind.InvestigateOnly)]
    public void PlannerSelectsAdvisoryAction(string reason, RecoveryActionKind expected)
    {
        var plan = new RecoveryPlanner().Propose(Incident(reason));
        Assert.True(plan.RequiresHumanApproval);
        var action = Assert.Single(plan.Actions);
        Assert.Equal(expected, action.Kind);
        Assert.Equal("incident-1", action.IncidentId);
    }

    [Theory]
    [InlineData(RecoveryActionKind.RestartService, null)]
    [InlineData(RecoveryActionKind.ScaleOut, 2)]
    [InlineData(RecoveryActionKind.InvestigateOnly, null)]
    public void DryRunNeverExecutes(RecoveryActionKind kind, int? target)
    {
        var action = new RecoveryAction("action-1", "incident-1", "node-1",
            kind, "Advisory only", target);
        var result = new RecoveryDryRunService().Simulate(action);
        Assert.False(result.Executed);
        Assert.NotEmpty(result.PredictedEffect);
        Assert.Contains(result.SafetyChecks, s => s.Contains("approval", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    public void RejectsUnsafeScaleTargets(int target)
    {
        var action = new RecoveryAction("action-1", "incident-1", "node-1",
            RecoveryActionKind.ScaleOut, "Scale", target);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryDryRunService().Simulate(action));
    }

    [Fact]
    public void RejectsUnexpectedTargetOnRestart()
    {
        var action = new RecoveryAction("action-1", "incident-1", "node-1",
            RecoveryActionKind.RestartService, "Restart", 3);
        Assert.Throws<ArgumentException>(() => new RecoveryDryRunService().Simulate(action));
    }

    [Fact]
    public void RejectsUnknownActionKind()
    {
        var action = new RecoveryAction("action-1", "incident-1", "node-1",
            (RecoveryActionKind)999, "Unknown");
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryDryRunService().Simulate(action));
    }
}
