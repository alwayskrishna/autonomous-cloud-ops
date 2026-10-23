using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AutonomousCloudOps.Core;

/// <summary>Pull-based abstraction for telemetry providers, including future Azure Monitor adapters.</summary>
public interface ITelemetrySource
{
    IAsyncEnumerable<TelemetrySample> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Deterministic offline provider; yields one sample per configured item.</summary>
public sealed class SimulatedTelemetrySource : ITelemetrySource
{
    private readonly IReadOnlyList<TelemetrySample> _samples;

    public SimulatedTelemetrySource(IEnumerable<TelemetrySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _samples = samples.ToArray();
        if (_samples.Any(sample => sample is null))
            throw new ArgumentException("Telemetry samples cannot contain null.", nameof(samples));
    }

    public async IAsyncEnumerable<TelemetrySample> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var sample in _samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return sample;
            await Task.Yield();
        }
    }
}

public sealed record IngestionResult(int SamplesProcessed, IReadOnlyList<Incident> Incidents);

/// <summary>Consumes a bounded stream of telemetry and detects incidents using the core rule engine.</summary>
public sealed class TelemetryIngestionService
{
    private readonly IncidentDetector _detector;

    public TelemetryIngestionService(IncidentDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    public async Task<IngestionResult> IngestAsync(
        ITelemetrySource source,
        int maxSamples = 10_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maxSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSamples));

        using var activity = CloudOpsObservability.ActivitySource.StartActivity("telemetry.ingest");
        var stopwatch = Stopwatch.StartNew();
        var incidents = new List<Incident>();
        var processed = 0;
        await using var enumerator = source.ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (processed < maxSamples && await enumerator.MoveNextAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = enumerator.Current;
            if (sample is null)
                throw new InvalidOperationException("Telemetry source yielded a null sample.");
            incidents.AddRange(_detector.Detect(sample));
            processed++;
        }

        stopwatch.Stop();
        CloudOpsObservability.SamplesProcessed.Add(processed);
        CloudOpsObservability.IncidentsDetected.Add(incidents.Count);
        CloudOpsObservability.IngestionDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        activity?.SetTag("samples.processed", processed);
        activity?.SetTag("incidents.detected", incidents.Count);
        return new IngestionResult(processed, incidents);
    }
}
