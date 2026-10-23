using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class DurableApprovalTests
{
    [Fact]
    public void DecisionsSurviveStoreRecreation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"approval-{Guid.NewGuid():N}.db");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var action = new RecoveryAction("a", "incident", "node", RecoveryActionKind.InvestigateOnly, "Review");
            var first = new RecoveryApprovalStore(path);
            var submitted = first.Submit(action, now);
            first.Approve(submitted.Id, "reviewer", now.AddMinutes(1), "Approved for simulation");
            var reopened = new RecoveryApprovalStore(path);
            var record = reopened.Get(submitted.Id);
            Assert.Equal(RecoveryApprovalStatus.Approved, record.Status);
            Assert.Single(record.Decisions);
            Assert.Throws<InvalidOperationException>(() =>
                reopened.Reject(submitted.Id, "reviewer", now.AddMinutes(2), "No"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
