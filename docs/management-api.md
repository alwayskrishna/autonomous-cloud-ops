# Local management API (PR #7)

Run the HTTP service locally:

```bash
dotnet run --project src/AutonomousCloudOps.Api --urls http://127.0.0.1:5080
```

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/health` | Liveness response |
| GET | `/api/incidents` | List tracked incidents |
| GET | `/api/incidents/{id}` | Retrieve an incident |
| POST | `/api/telemetry/detect` | Detect and register incidents from a telemetry sample |
| POST | `/api/incidents/{id}/acknowledge` | Acknowledge an open incident |
| POST | `/api/incidents/{id}/resolve` | Resolve an open or acknowledged incident |

Example telemetry body: `{"resourceId":"demo/node","timestamp":"2026-01-01T00:00:00Z","cpuPercent":95,"memoryPercent":20,"http5xxCount":0}`.

Transition body: `{"actor":"local-operator","at":"2026-01-01T00:01:00Z","note":"investigating"}`.

**Security boundary:** This prototype has no authentication, authorization, rate limiting, or persistent storage. The actor field is not a verified identity. Bind only to loopback for development; do not expose publicly or deploy as-is. No Azure services are provisioned.
