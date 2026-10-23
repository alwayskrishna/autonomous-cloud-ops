using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class TelemetryIngestionTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task IngestsBatchAndCollectsIncidents()
    {
        var source = new SimulatedTelemetrySource(new[]
        {
            new TelemetrySample("node/a", Time, 10, 20, 0),
            new TelemetrySample("node/b", Time, 95, 95, 11)
        });
        var result = await new TelemetryIngestionService(new IncidentDetector()).IngestAsync(source);
        Assert.Equal(2, result.SamplesProcessed);
        Assert.Equal(3, result.Incidents.Count);
        Assert.All(result.Incidents, incident => Assert.Equal("node/b", incident.ResourceId));
    }

    [Fact]
    public async Task StopsAtConfiguredBatchLimit()
    {
        var source = new SimulatedTelemetrySource(Enumerable.Range(0, 100)
            .Select(i => new TelemetrySample($"node/{i}", Time, 95, 20, 0)));
        var result = await new TelemetryIngestionService(new IncidentDetector()).IngestAsync(source, maxSamples: 3);
        Assert.Equal(3, result.SamplesProcessed);
        Assert.Equal(3, result.Incidents.Count);
    }

    [Fact]
    public async Task EmptyStreamReturnsEmptyResult()
    {
        var result = await new TelemetryIngestionService(new IncidentDetector())
            .IngestAsync(new SimulatedTelemetrySource(Array.Empty<TelemetrySample>()));
        Assert.Equal(0, result.SamplesProcessed);
        Assert.Empty(result.Incidents);
    }

    [Fact]
    public async Task RejectsInvalidLimit()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new TelemetryIngestionService(new IncidentDetector())
                .IngestAsync(new SimulatedTelemetrySource(Array.Empty<TelemetrySample>()), maxSamples: 0));
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TelemetryIngestionService(new IncidentDetector())
                .IngestAsync(new SimulatedTelemetrySource(new[]
                {
                    new TelemetrySample("node/a", Time, 95, 20, 0)
                }), cancellationToken: cts.Token));
    }
}
