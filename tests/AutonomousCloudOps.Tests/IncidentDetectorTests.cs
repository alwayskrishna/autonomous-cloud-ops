using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class IncidentDetectorTests
{
    private readonly IncidentDetector _detector = new();

    [Fact]
    public void HealthySampleProducesNoIncidents()
    {
        Assert.Empty(_detector.Detect(Sample(20, 30, 0)));
    }

    [Fact]
    public void ThresholdsProduceExpectedIncidents()
    {
        var incidents = _detector.Detect(Sample(90, 90, 10));
        Assert.Equal(3, incidents.Count);
        Assert.Equal(2, incidents.Count(i => i.Severity == IncidentSeverity.Critical));
        Assert.Single(incidents, i => i.Severity == IncidentSeverity.Warning);
        Assert.All(incidents, i => Assert.Equal("demo/resource", i.ResourceId));
    }

    [Theory]
    [InlineData(-1, 10, 0)]
    [InlineData(101, 10, 0)]
    [InlineData(10, -1, 0)]
    [InlineData(10, 101, 0)]
    [InlineData(10, 10, -1)]
    public void InvalidTelemetryIsRejected(double cpu, double memory, int errors)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _detector.Detect(Sample(cpu, memory, errors)));
    }

    [Fact]
    public void MissingResourceIsRejected()
    {
        Assert.Throws<ArgumentException>(() => _detector.Detect(new TelemetrySample("", DateTimeOffset.UtcNow, 10, 10, 0)));
    }

    private static TelemetrySample Sample(double cpu, double memory, int errors) =>
        new("demo/resource", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), cpu, memory, errors);
}
