using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class RecoveryApprovalTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static RecoveryAction Action() => new("action-1", "incident-1", "node-1",
        RecoveryActionKind.ScaleOut, "CPU elevated", 2);

    [Fact]
    public void ApprovalCapturesIdentityReasonAndTimestamp()
    {
        var store = new RecoveryApprovalStore();
        var pending = store.Submit(Action(), Now);
        Assert.Equal(RecoveryApprovalStatus.Pending, pending.Status);
        Assert.Empty(pending.Decisions);
        var approved = store.Approve(pending.Id, "reviewer", Now.AddMinutes(1), "Verified capacity");
        Assert.Equal(RecoveryApprovalStatus.Approved, approved.Status);
        var decision = Assert.Single(approved.Decisions);
        Assert.Equal("reviewer", decision.Actor);
        Assert.Equal("Verified capacity", decision.Reason);
        Assert.Equal(Now.AddMinutes(1), decision.At);
        Assert.Equal(RecoveryApprovalStatus.Approved, store.Get(pending.Id).Status);
    }

    [Fact]
    public void RejectionIsTerminal()
    {
        var store = new RecoveryApprovalStore();
        var pending = store.Submit(Action(), Now);
        store.Reject(pending.Id, "reviewer", Now.AddMinutes(1), "Risk too high");
        Assert.Throws<InvalidOperationException>(() =>
            store.Approve(pending.Id, "other", Now.AddMinutes(2), "Override"));
        Assert.Equal(RecoveryApprovalStatus.Rejected, store.Get(pending.Id).Status);
    }

    [Fact]
    public void ApprovalCannotBeRepeated()
    {
        var store = new RecoveryApprovalStore();
        var pending = store.Submit(Action(), Now);
        store.Approve(pending.Id, "reviewer", Now, "Checked");
        Assert.Throws<InvalidOperationException>(() =>
            store.Approve(pending.Id, "reviewer", Now, "Again"));
    }

    [Fact]
    public void DecisionMustFollowSubmissionAndHaveActorAndReason()
    {
        var store = new RecoveryApprovalStore();
        var pending = store.Submit(Action(), Now);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Approve(pending.Id, "reviewer", Now.AddSeconds(-1), "Checked"));
        Assert.Throws<ArgumentException>(() =>
            store.Approve(pending.Id, "", Now, "Checked"));
        Assert.Throws<ArgumentException>(() =>
            store.Approve(pending.Id, "reviewer", Now, ""));
        Assert.Equal(RecoveryApprovalStatus.Pending, store.Get(pending.Id).Status);
    }

    [Fact]
    public void RejectsInvalidActionAndMissingApproval()
    {
        var store = new RecoveryApprovalStore();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Submit(
            new RecoveryAction("a", "i", "r", RecoveryActionKind.ScaleOut, "bad", 100), Now));
        Assert.Throws<KeyNotFoundException>(() => store.Get("missing"));
    }

    [Fact]
    public async Task OnlyOneConcurrentDecisionCanSucceed()
    {
        var store = new RecoveryApprovalStore();
        var pending = store.Submit(Action(), Now);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            try
            {
                store.Approve(pending.Id, "reviewer", Now.AddMinutes(1), "Checked");
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }));
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(succeeded => succeeded));
    }
}
