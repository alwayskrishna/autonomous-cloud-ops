using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class IncidentEvidenceTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
    private static Incident Incident() => new("i-1", "node-a", Time, IncidentSeverity.Critical, "CPU");
    private static EvidenceRecord Record(string id, string resource, int offset, EvidenceKind kind = EvidenceKind.Metric) =>
        new(id, resource, Time.AddMinutes(offset), kind, $"Observation {id}");

    [Fact]
    public async Task RetrievesOnlyMatchingResourceAndTimeWindow()
    {
        var source = new InMemoryIncidentEvidenceSource(new[]
        {
            Record("before", "node-a", -31),
            Record("cpu", "node-a", -10),
            Record("deploy", "node-a", -1, EvidenceKind.Deployment),
            Record("other", "node-b", -1),
            Record("after", "node-a", 6)
        });
        var bundle = await new IncidentEvidenceService(source).RetrieveAsync(Incident());
        Assert.Equal(new[] { "cpu", "deploy" }, bundle.Records.Select(r => r.Id));
        Assert.Equal(Time.AddMinutes(-30), bundle.WindowStart);
        Assert.Equal(Time.AddMinutes(5), bundle.WindowEnd);
    }

    [Fact]
    public async Task IncludesBoundaryTimestamps()
    {
        var source = new InMemoryIncidentEvidenceSource(new[]
        {
            Record("start", "node-a", -30), Record("end", "node-a", 5)
        });
        Assert.Equal(2, (await new IncidentEvidenceService(source).RetrieveAsync(Incident())).Records.Count);
    }

    [Fact]
    public async Task AppliesRecordLimitDeterministically()
    {
        var source = new InMemoryIncidentEvidenceSource(new[]
        {
            Record("c", "node-a", 0), Record("a", "node-a", 0), Record("b", "node-a", 0)
        });
        var bundle = await new IncidentEvidenceService(source, maxRecords: 2).RetrieveAsync(Incident());
        Assert.Equal(new[] { "a", "b" }, bundle.Records.Select(r => r.Id));
    }

    [Fact]
    public async Task EmptyEvidenceIsValid()
    {
        var bundle = await new IncidentEvidenceService(new InMemoryIncidentEvidenceSource([])).RetrieveAsync(Incident());
        Assert.Empty(bundle.Records);
    }

    [Fact]
    public void InvalidRecordLimitIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IncidentEvidenceService(new InMemoryIncidentEvidenceSource([]), maxRecords: 0));
    }

    [Fact]
    public async Task CancellationIsHonored()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new IncidentEvidenceService(new InMemoryIncidentEvidenceSource([]))
                .RetrieveAsync(Incident(), cts.Token));
    }
}
