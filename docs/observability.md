# OpenTelemetry-compatible observability (PR #6)

The core library exposes standard .NET `ActivitySource` and `Meter` primitives through `CloudOpsObservability`. These are the APIs consumed by OpenTelemetry .NET instrumentation, and they do not require an exporter to operate.

## Signals

| Signal | Name | Description |
| --- | --- | --- |
| Trace | `telemetry.ingest` | Bounded ingestion operation |
| Trace | `incident.correlate` | Alert grouping operation |
| Counter | `cloudops.telemetry.samples_processed` | Samples read |
| Counter | `cloudops.incidents.detected` | Incidents detected during ingestion |
| Counter | `cloudops.incidents.correlated` | Correlation events |
| Histogram | `cloudops.ingestion.duration` | End-to-end ingestion latency in milliseconds |

Activity tags use low-cardinality values only, such as sample counts and incident counts. Avoid tagging raw resource IDs, incident IDs or exception messages on metric instruments to prevent cardinality and sensitive-data issues.

This PR does **not** install an OTLP exporter or send telemetry to Azure. A future API/hosting layer can register OpenTelemetry SDK exporters, sampling, and resource attributes explicitly.
