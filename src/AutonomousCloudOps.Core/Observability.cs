using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AutonomousCloudOps.Core;

/// <summary>
/// Standard .NET ActivitySource and Meter instrumentation, compatible with OpenTelemetry.
/// Exporters are deliberately not configured here: no network or Azure dependency.
/// </summary>
public static class CloudOpsObservability
{
    public const string Name = "AutonomousCloudOps";
    public const string Version = "1.0.0";

    public static readonly ActivitySource ActivitySource = new(Name, Version);
    public static readonly Meter Meter = new(Name, Version);

    public static readonly Counter<long> SamplesProcessed =
        Meter.CreateCounter<long>("cloudops.telemetry.samples_processed", unit: "{sample}",
            description: "Number of telemetry samples processed.");

    public static readonly Counter<long> IncidentsDetected =
        Meter.CreateCounter<long>("cloudops.incidents.detected", unit: "{incident}",
            description: "Number of incidents detected.");

    public static readonly Counter<long> CorrelationEvents =
        Meter.CreateCounter<long>("cloudops.incidents.correlated", unit: "{event}",
            description: "Number of incident events added to correlation groups.");

    public static readonly Histogram<double> IngestionDuration =
        Meter.CreateHistogram<double>("cloudops.ingestion.duration", unit: "ms",
            description: "Telemetry ingestion duration in milliseconds.");
}
