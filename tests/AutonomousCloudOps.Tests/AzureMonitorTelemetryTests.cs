using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class AzureMonitorTelemetryTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static AzureMetricPoint Point(string metric, double value, string resource = "azure/vm1", int seconds = 0) =>
        new(resource, metric, Time.AddSeconds(seconds), value);

    [Fact]
    public async Task MapsMetricsIntoTelemetryAndDetectsIncidents()
    {
        var source = new AzureMonitorTelemetrySource(new MockAzureMetricReader(new[]
        {
            Point("Percentage CPU", 95),
            Point("Memory Percentage", 50),
            Point("Http 5xx", 11)
        }));
        var result = await new TelemetryIngestionService(new IncidentDetector()).IngestAsync(source);
        Assert.Equal(1, result.SamplesProcessed);
        Assert.Equal(2, result.Incidents.Count);
    }

    [Fact]
    public async Task GroupsConsecutiveResourceAndTimestampBuckets()
    {
        var source = new AzureMonitorTelemetrySource(new MockAzureMetricReader(new[]
        {
            Point("Percentage CPU", 90),
            Point("Percentage CPU", 20, seconds: 60),
            Point("Memory Percentage", 92, seconds: 60)
        }));
        var samples = await Collect(source);
        Assert.Equal(2, samples.Count);
        Assert.Equal(90, samples[0].CpuPercent);
        Assert.Equal(92, samples[1].MemoryPercent);
    }

    [Fact]
    public async Task MissingMetricsDefaultToZero()
    {
        var source = new AzureMonitorTelemetrySource(new MockAzureMetricReader(new[] { Point("Http 5xx", 15) }));
        var sample = Assert.Single(await Collect(source));
        Assert.Equal(0, sample.CpuPercent);
        Assert.Equal(15, sample.Http5xxCount);
    }

    [Fact]
    public async Task DuplicateMetricsAreRejected()
    {
        var source = new AzureMonitorTelemetrySource(new MockAzureMetricReader(new[]
        {
            Point("Percentage CPU", 90), Point("Percentage CPU", 91)
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Collect(source));
    }

    [Theory]
    [InlineData("Percentage CPU", 101)]
    [InlineData("Http 5xx", 1.5)]
    [InlineData("Unknown", 10)]
    public async Task InvalidMetricsAreRejected(string name, double value)
    {
        var source = new AzureMonitorTelemetrySource(new MockAzureMetricReader(new[] { Point(name, value) }));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await Collect(source));
    }

    [Fact]
    public async Task CancellationIsHonored()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var source = new AzureMonitorTelemetrySource(new MockAzureMetricReader(new[] { Point("Percentage CPU", 95) }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Collect(source, cts.Token));
    }

    private static async Task<List<TelemetrySample>> Collect(ITelemetrySource source, CancellationToken ct = default)
    {
        var samples = new List<TelemetrySample>();
        await foreach (var sample in source.ReadAsync(ct))
            samples.Add(sample);
        return samples;
    }
}
