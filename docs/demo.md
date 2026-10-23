# Guided offline demo

No cloud account is needed. Use the .NET 10 SDK.

## 1. Run the incident detection demo

```bash
dotnet run --project src/AutonomousCloudOps.Cli/AutonomousCloudOps.Cli.csproj
```

The simulated telemetry includes one high-CPU/high-error resource, one healthy resource and one high-memory resource. The CLI detects alerts, correlates incidents, opens them in the lifecycle store, and acknowledges/resolves the first incident. The output is JSON with processed samples, correlated alerts and incident state history.

## 2. Run the local API

```bash
dotnet run --project src/AutonomousCloudOps.Api/AutonomousCloudOps.Api.csproj --urls http://127.0.0.1:5080
```

In another terminal:

```bash
curl http://127.0.0.1:5080/health
curl -X POST http://127.0.0.1:5080/api/telemetry/detect \
  -H 'Content-Type: application/json' \
  -d '{"resourceId":"demo-node","timestamp":"2026-10-23T12:00:00Z","cpuPercent":96,"memoryPercent":40,"http5xxCount":0}'
curl http://127.0.0.1:5080/api/incidents
```

Copy the returned incident ID into the following calls:

```bash
curl -X POST http://127.0.0.1:5080/api/incidents/INCIDENT_ID/diagnose
curl -X POST http://127.0.0.1:5080/api/incidents/INCIDENT_ID/recovery/dry-run
curl -X POST http://127.0.0.1:5080/api/incidents/INCIDENT_ID/recovery/workflows
```

The workflow returns a pending approval ID. To record a decision (without executing any action):

```bash
curl -X POST http://127.0.0.1:5080/api/recovery/approvals/APPROVAL_ID/approve \
  -H 'Content-Type: application/json' \
  -d '{"actor":"demo-reviewer","at":"2026-10-24T12:00:00Z","reason":"Reviewed simulation only"}'
```

Use a decision timestamp later than the approval's `requestedAt`; adjust the example as necessary. Read the workflow again to see the status transition.

## 3. Run correctness and synthetic load tests

```bash
dotnet test tests/AutonomousCloudOps.Tests/AutonomousCloudOps.Tests.csproj -c Release
dotnet test tests/AutonomousCloudOps.Api.Tests/AutonomousCloudOps.Api.Tests.csproj -c Release
dotnet run --project benchmarks/AutonomousCloudOps.Benchmarks/AutonomousCloudOps.Benchmarks.csproj -c Release -- 10000
```

## What this demo does not do

It does not call an LLM, connect to live Azure Monitor, implement the MCP wire protocol, run Azure Durable Functions, or restart/scale infrastructure. Human approvals are recorded locally but never execute actions. Keep the API bound to localhost because authentication is not implemented.
