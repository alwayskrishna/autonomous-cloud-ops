using System.Diagnostics;
using AutonomousCloudOps.Core;

var count = args.Length == 0 ? 10_000 : int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
if (count is < 100 or > 100_000)
{
    Console.Error.WriteLine("Sample count must be between 100 and 100000.");
    return 1;
}

var now = DateTimeOffset.UtcNow;
var samples = Enumerable.Range(0, count).Select(i =>
    new TelemetrySample($"node-{i % 20}", now.AddMilliseconds(i), i % 100, 25, i % 100 == 0 ? 20 : 0));
var service = new TelemetryIngestionService(new IncidentDetector());
var stopwatch = Stopwatch.StartNew();
var result = await service.IngestAsync(new SimulatedTelemetrySource(samples), maxSamples: count);
stopwatch.Stop();
var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.000001);
Console.WriteLine($"Samples: {result.SamplesProcessed}");
Console.WriteLine($"Incidents: {result.Incidents.Count}");
Console.WriteLine($"ElapsedMs: {stopwatch.Elapsed.TotalMilliseconds:F2}");
Console.WriteLine($"SamplesPerSecond: {result.SamplesProcessed / seconds:F2}");
return 0;
