using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class ResilienceTests
{
    [Fact]
    public async Task TransientFailureRecoversWithinBoundedAttempts()
    {
        var attempts = 0;
        var result = await new ResilientOperation(3, TimeSpan.Zero).RunAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3) throw new IOException("Temporary");
                return Task.FromResult("ok");
            },
            ex => ex is IOException);
        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task PermanentFailureIsNotRetried()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ResilientOperation(5, TimeSpan.Zero).RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new InvalidOperationException("Permanent");
                },
                ex => ex is IOException));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task TransientFailuresStopAtLimit()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<IOException>(() =>
            new ResilientOperation(2, TimeSpan.Zero).RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new IOException("Still failing");
                },
                ex => ex is IOException));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task CancellationNeverRetries()
    {
        var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ResilientOperation(3, TimeSpan.Zero).RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new OperationCanceledException();
                },
                _ => true));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task IngestionHonorsMaximumSampleCountUnderLoad()
    {
        var samples = Enumerable.Range(0, 2_000).Select(i =>
            new TelemetrySample("node-a", DateTimeOffset.UnixEpoch.AddSeconds(i), 5, 5, 0));
        var result = await new TelemetryIngestionService(new IncidentDetector())
            .IngestAsync(new SimulatedTelemetrySource(samples), maxSamples: 150);
        Assert.Equal(150, result.SamplesProcessed);
    }

    [Fact]
    public async Task ConcurrentIncidentReadsRemainConsistent()
    {
        var store = new IncidentLifecycleStore();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 100; i++)
            store.Open(new Incident($"incident-{i}", "node-a", now, IncidentSeverity.Warning, "Test"));
        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
                Assert.Equal(100, store.List().Count);
        }));
        await Task.WhenAll(tasks);
        Assert.Equal(100, store.List().Count);
    }

    [Fact]
    public void RejectsUnboundedRetryConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResilientOperation(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResilientOperation(11));
    }
}
