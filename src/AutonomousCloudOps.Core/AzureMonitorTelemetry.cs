using System.Runtime.CompilerServices;

namespace AutonomousCloudOps.Core;

/// <summary>Provider-neutral representation of Azure Monitor metric datapoints.</summary>
public sealed record AzureMetricPoint(
    string ResourceId,
    string MetricName,
    DateTimeOffset Timestamp,
    double Value);

/// <summary>Async metric reader boundary; production Azure SDK adapter can implement this later.</summary>
public interface IAzureMetricReader
{
    IAsyncEnumerable<AzureMetricPoint> ReadMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Deterministic in-memory Azure Monitor-like metric reader with no cloud dependency.</summary>
public sealed class MockAzureMetricReader : IAzureMetricReader
{
    private readonly IReadOnlyList<AzureMetricPoint> _points;

    public MockAzureMetricReader(IEnumerable<AzureMetricPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points.ToArray();
        if (_points.Any(p => p is null))
            throw new ArgumentException("Metric points cannot contain null.", nameof(points));
    }

    public async IAsyncEnumerable<AzureMetricPoint> ReadMetricsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var point in _points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return point;
            await Task.Yield();
        }
    }
}

/// <summary>
/// Transforms a sorted metric stream into telemetry samples, one per resource and timestamp.
/// Metric names: Percentage CPU, Memory Percentage, Http 5xx.
/// </summary>
public sealed class AzureMonitorTelemetrySource : ITelemetrySource
{
    private readonly IAzureMetricReader _reader;

    public AzureMonitorTelemetrySource(IAzureMetricReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public async IAsyncEnumerable<TelemetrySample> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? resource = null;
        DateTimeOffset timestamp = default;
        double cpu = 0;
        double memory = 0;
        int errors = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var point in _reader.ReadMetricsAsync(cancellationToken).WithCancellation(cancellationToken))
        {
            Validate(point);
            if (resource is not null &&
                (!string.Equals(resource, point.ResourceId, StringComparison.Ordinal) || timestamp != point.Timestamp))
            {
                yield return new TelemetrySample(resource, timestamp, cpu, memory, errors);
                cpu = 0;
                memory = 0;
                errors = 0;
                seen.Clear();
            }

            resource = point.ResourceId;
            timestamp = point.Timestamp;
            if (!seen.Add(point.MetricName))
                throw new InvalidOperationException($"Duplicate metric '{point.MetricName}' for resource and timestamp.");

            switch (point.MetricName.ToLowerInvariant())
            {
                case "percentage cpu": cpu = point.Value; break;
                case "memory percentage": memory = point.Value; break;
                case "http 5xx": errors = checked((int)point.Value); break;
            }
        }
        if (resource is not null)
            yield return new TelemetrySample(resource, timestamp, cpu, memory, errors);
    }

    private static void Validate(AzureMetricPoint point)
    {
        if (point is null || string.IsNullOrWhiteSpace(point.ResourceId) || string.IsNullOrWhiteSpace(point.MetricName))
            throw new ArgumentException("Metric point and resource ID are required.");
        if (!double.IsFinite(point.Value) || point.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(point), "Metric value must be finite and nonnegative.");
        switch (point.MetricName.ToLowerInvariant())
        {
            case "percentage cpu":
            case "memory percentage":
                if (point.Value > 100)
                    throw new ArgumentOutOfRangeException(nameof(point), "Percentage metric exceeds 100.");
                break;
            case "http 5xx":
                if (point.Value > int.MaxValue || point.Value != Math.Truncate(point.Value))
                    throw new ArgumentOutOfRangeException(nameof(point), "HTTP error count must be a nonnegative integer.");
                break;
            default:
                throw new ArgumentException($"Unsupported metric name: {point.MetricName}", nameof(point));
        }
    }
}
