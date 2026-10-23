using System.Diagnostics;
using System.Diagnostics.Metrics;
using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class ObservabilityTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task IngestionEmitsMetrics()
    {
        var measurements = new List<(string Name, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == CloudOpsObservability.Name)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (measurements) measurements.Add((instrument.Name, value));
        });
        listener.Start();

        var source = new SimulatedTelemetrySource(new[]
        {
            new TelemetrySample("resource-1", Time, 95, 20, 0),
            new TelemetrySample("resource-2", Time, 10, 20, 0)
        });
        await new TelemetryIngestionService(new IncidentDetector()).IngestAsync(source);

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Name == "cloudops.telemetry.samples_processed" && m.Value == 2);
            Assert.Contains(measurements, m => m.Name == "cloudops.incidents.detected" && m.Value == 1);
        }
    }

    [Fact]
    public async Task IngestionCreatesTraceActivity()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CloudOpsObservability.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        await new TelemetryIngestionService(new IncidentDetector())
            .IngestAsync(new SimulatedTelemetrySource(Array.Empty<TelemetrySample>()));
        Assert.Contains(activities, a => a.OperationName == "telemetry.ingest");
    }

    [Fact]
    public void CorrelationCreatesActivityAndCounter()
    {
        var observed = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "cloudops.incidents.correlated")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => observed.Add(value));
        listener.Start();

        new IncidentCorrelator().Add(new Incident("id", "resource", Time, IncidentSeverity.Warning, "Rule cpu: CpuPercent 95 >= 90"));
        Assert.Contains(1L, observed);
    }
}
