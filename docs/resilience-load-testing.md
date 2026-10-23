# Resilience, evaluation and local load testing (PR #19)

This milestone introduces `ResilientOperation`, a bounded asynchronous retry primitive. It retries **only** errors explicitly classified as transient, stops at the configured attempt limit, honors cancellation, and rejects unbounded configurations. The default is three attempts with 50 ms delay. It is a reusable building block; existing providers are not automatically retried.

Automated tests cover transient recovery, permanent failures, exhausted retries, cancellation, bounded telemetry ingestion and concurrent incident reads. They are deterministic offline correctness tests, not production-scale throughput guarantees.

## Run tests

```bash
dotnet test tests/AutonomousCloudOps.Tests/AutonomousCloudOps.Tests.csproj -c Release
dotnet test tests/AutonomousCloudOps.Api.Tests/AutonomousCloudOps.Api.Tests.csproj -c Release
```

## Local performance benchmark

```bash
dotnet run --project benchmarks/AutonomousCloudOps.Benchmarks/AutonomousCloudOps.Benchmarks.csproj -c Release -- 10000
```

The benchmark prints sample count, elapsed milliseconds, samples per second and incident count. Results vary by machine and are not service-level objectives. The CLI is bounded to 100–100,000 synthetic samples to avoid accidental unbounded runs.

No live Azure Monitor, paid APIs or infrastructure are used. Do not run high-volume tests against a publicly exposed API; the API still lacks authentication and tenant-scoped authorization.
