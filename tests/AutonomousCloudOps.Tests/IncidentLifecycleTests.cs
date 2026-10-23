using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class IncidentLifecycleTests
{
    private static readonly DateTimeOffset Detected = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    private static Incident NewIncident(string id = "incident-1") =>
        new(id, "resource-1", Detected, IncidentSeverity.Critical, "CPU high");

    [Fact]
    public void OpenAcknowledgeResolveRecordsAuditHistory()
    {
        var store = new IncidentLifecycleStore();
        store.Open(NewIncident());
        var acknowledged = store.Acknowledge("incident-1", "operator", Detected.AddMinutes(1), "Investigating");
        Assert.Equal(IncidentStatus.Acknowledged, acknowledged.Status);
        var resolved = store.Resolve("incident-1", "operator", Detected.AddMinutes(2), "Mitigated");
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.Equal(2, resolved.History.Count);
        Assert.Equal(IncidentStatus.Open, resolved.History[0].From);
        Assert.Equal(IncidentStatus.Acknowledged, resolved.History[0].To);
        Assert.Equal("Mitigated", resolved.History[1].Note);
        Assert.Equal("operator", resolved.History[1].Actor);
    }

    [Fact]
    public void OpenIncidentMayBeResolvedDirectly()
    {
        var store = new IncidentLifecycleStore();
        store.Open(NewIncident());
        var resolved = store.Resolve("incident-1", "system", Detected);
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.Single(resolved.History);
    }

    [Fact]
    public void DuplicateIncidentIsRejected()
    {
        var store = new IncidentLifecycleStore();
        store.Open(NewIncident());
        Assert.Throws<InvalidOperationException>(() => store.Open(NewIncident()));
    }

    [Fact]
    public void RepeatedAcknowledgmentAndChangesAfterResolutionAreRejected()
    {
        var store = new IncidentLifecycleStore();
        store.Open(NewIncident());
        store.Acknowledge("incident-1", "operator", Detected);
        Assert.Throws<InvalidOperationException>(() => store.Acknowledge("incident-1", "operator", Detected));
        store.Resolve("incident-1", "operator", Detected);
        Assert.Throws<InvalidOperationException>(() => store.Resolve("incident-1", "operator", Detected));
    }

    [Fact]
    public void TransitionTimeCannotGoBackwards()
    {
        var store = new IncidentLifecycleStore();
        store.Open(NewIncident());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Acknowledge("incident-1", "operator", Detected.AddSeconds(-1)));
        store.Acknowledge("incident-1", "operator", Detected.AddMinutes(2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Resolve("incident-1", "operator", Detected.AddMinutes(1)));
    }

    [Fact]
    public void UnknownIncidentIsRejected()
    {
        var store = new IncidentLifecycleStore();
        Assert.Throws<KeyNotFoundException>(() => store.Get("missing"));
        Assert.Throws<KeyNotFoundException>(() => store.Resolve("missing", "operator", Detected));
    }

    [Fact]
    public void ListingReturnsSnapshotOfTrackedIncidents()
    {
        var store = new IncidentLifecycleStore();
        store.Open(NewIncident("a"));
        store.Open(NewIncident("b"));
        var snapshot = store.List();
        Assert.Equal(2, snapshot.Count);
        store.Resolve("a", "operator", Detected);
        Assert.Equal(IncidentStatus.Open, snapshot[0].Status);
        Assert.Equal(IncidentStatus.Resolved, store.Get("a").Status);
    }

    [Fact]
    public async Task ConcurrentAcknowledgmentAllowsExactlyOneWinner()
    {
        var store = new IncidentLifecycleStore();
        store.Open(NewIncident());
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            try
            {
                store.Acknowledge("incident-1", $"operator-{i}", Detected);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        })));
        Assert.Single(results, result => result);
        Assert.Single(store.Get("incident-1").History);
    }
}
