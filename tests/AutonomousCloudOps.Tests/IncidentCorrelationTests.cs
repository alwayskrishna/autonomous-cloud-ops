using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class IncidentCorrelationTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static Incident Event(string id, string resource, string rule, int seconds,
        IncidentSeverity severity = IncidentSeverity.Warning, int value = 91) =>
        new(id, resource, Time.AddSeconds(seconds), severity, $"Rule {rule}: CpuPercent {value} >= 90");

    [Fact]
    public void SameResourceAndRuleWithinWindowAreDeduplicated()
    {
        var correlator = new IncidentCorrelator(TimeSpan.FromMinutes(5));
        var first = correlator.Add(Event("one", "node-a", "cpu", 0));
        var second = correlator.Add(Event("two", "node-a", "cpu", 30, value: 98));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.OccurrenceCount);
        Assert.Equal(Time, second.FirstSeen);
        Assert.Equal(Time.AddSeconds(30), second.LastSeen);
        Assert.Contains("98", second.LatestReason);
        Assert.Single(correlator.Snapshot());
    }

    [Fact]
    public void DifferentResourcesOrRulesRemainSeparate()
    {
        var correlator = new IncidentCorrelator();
        correlator.Add(Event("one", "node-a", "cpu", 0));
        correlator.Add(Event("two", "node-b", "cpu", 0));
        correlator.Add(Event("three", "node-a", "memory", 0));
        Assert.Equal(3, correlator.Snapshot().Count);
    }

    [Fact]
    public void ExpiredWindowStartsNewGroup()
    {
        var correlator = new IncidentCorrelator(TimeSpan.FromSeconds(30));
        correlator.Add(Event("one", "node-a", "cpu", 0));
        var fresh = correlator.Add(Event("two", "node-a", "cpu", 31));
        Assert.Equal("two", fresh.Id);
        Assert.Equal(1, fresh.OccurrenceCount);
    }

    [Fact]
    public void WindowIsInclusiveAndBasedOnLastSeen()
    {
        var correlator = new IncidentCorrelator(TimeSpan.FromSeconds(30));
        correlator.Add(Event("one", "node-a", "cpu", 0));
        correlator.Add(Event("two", "node-a", "cpu", 30));
        var third = correlator.Add(Event("three", "node-a", "cpu", 60));
        Assert.Equal(3, third.OccurrenceCount);
    }

    [Fact]
    public void SeverityEscalatesButNeverDowngrades()
    {
        var correlator = new IncidentCorrelator();
        correlator.Add(Event("one", "node-a", "cpu", 0));
        correlator.Add(Event("two", "node-a", "cpu", 10, IncidentSeverity.Critical));
        var result = correlator.Add(Event("three", "node-a", "cpu", 20));
        Assert.Equal(IncidentSeverity.Critical, result.Severity);
    }

    [Fact]
    public void OutOfOrderEventsForSameKeyAreRejected()
    {
        var correlator = new IncidentCorrelator();
        correlator.Add(Event("one", "node-a", "cpu", 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => correlator.Add(Event("two", "node-a", "cpu", 10)));
    }

    [Fact]
    public void InvalidWindowIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IncidentCorrelator(TimeSpan.Zero));
    }

    [Fact]
    public async Task ConcurrentAddsDoNotLoseOccurrences()
    {
        var correlator = new IncidentCorrelator();
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
            correlator.Add(Event(i.ToString(), "node-a", "cpu", 0))));
        await Task.WhenAll(tasks);
        Assert.Equal(50, Assert.Single(correlator.Snapshot()).OccurrenceCount);
    }

    [Fact]
    public void LegacyDetectorReasonsAlsoDeduplicate()
    {
        var correlator = new IncidentCorrelator();
        correlator.Add(new Incident("one", "node-a", Time, IncidentSeverity.Critical, "CPU utilization >= 90%"));
        correlator.Add(new Incident("two", "node-a", Time.AddSeconds(1), IncidentSeverity.Critical, "CPU utilization >= 90%"));
        Assert.Equal(2, Assert.Single(correlator.Snapshot()).OccurrenceCount);
    }
}
