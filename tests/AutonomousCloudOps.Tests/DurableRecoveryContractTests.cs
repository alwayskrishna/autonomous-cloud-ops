using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class DurableRecoveryContractTests
{
    [Fact]
    public async Task ValidTransitionSequenceAndCheckpointRead()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new DurableRecoveryCheckpoint("w1", "i1", DurableRecoveryStage.Created, now);
        var machine = new RecoveryOrchestrationStateMachine();
        state = machine.Advance(state, DurableRecoveryStage.Diagnosing, now.AddSeconds(1));
        state = machine.Advance(state, DurableRecoveryStage.AwaitingApproval, now.AddSeconds(2));
        Assert.Throws<InvalidOperationException>(() =>
            machine.Advance(state, DurableRecoveryStage.Completed, now.AddSeconds(3)));
        state = machine.Advance(state, DurableRecoveryStage.Approved, now.AddSeconds(3), "approval-1");
        state = machine.Advance(state, DurableRecoveryStage.Completed, now.AddSeconds(4));
        var store = new InMemoryRecoveryCheckpointStore();
        await store.WriteAsync(state);
        Assert.Equal(DurableRecoveryStage.Completed, (await store.ReadAsync("w1"))?.Stage);
    }
}
