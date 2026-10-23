using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class IncidentPersistenceTests
{
    [Fact]
    public void IncidentAndAuditHistorySurviveStoreRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cloudops-{Guid.NewGuid():N}.db");
        try
        {
            var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var incident = new Incident("persist-1", "node-1", now, IncidentSeverity.Critical, "CPU");
            var first = new IncidentLifecycleStore(path);
            first.Open(incident);
            first.Acknowledge(incident.Id, "operator", now.AddMinutes(1), "triage");
            var restarted = new IncidentLifecycleStore(path);
            var restored = restarted.Get(incident.Id);
            Assert.Equal(IncidentStatus.Acknowledged, restored.Status);
            Assert.Equal("triage", Assert.Single(restored.History).Note);
            restarted.Resolve(incident.Id, "operator", now.AddMinutes(2), "fixed");
            var secondRestart = new IncidentLifecycleStore(path);
            Assert.Equal(IncidentStatus.Resolved, secondRestart.Get(incident.Id).Status);
            Assert.Equal(2, secondRestart.Get(incident.Id).History.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DuplicateIdsRemainRejectedAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cloudops-{Guid.NewGuid():N}.db");
        try
        {
            var incident = new Incident("duplicate", "node", DateTimeOffset.UtcNow, IncidentSeverity.Warning, "errors");
            new IncidentLifecycleStore(path).Open(incident);
            Assert.Throws<InvalidOperationException>(() => new IncidentLifecycleStore(path).Open(incident));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DefaultStoreRemainsEphemeral()
    {
        var store = new IncidentLifecycleStore();
        Assert.Empty(store.List());
    }
}
