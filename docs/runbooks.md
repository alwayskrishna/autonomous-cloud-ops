# Operational runbooks (offline prototype)

## Runbook 1 — Incident triage

1. Confirm `GET /health` returns healthy on localhost.
2. Inspect `GET /api/incidents` and locate the incident ID, resource and severity.
3. Call `POST /api/incidents/{id}/diagnose` to retrieve **mock** diagnostic guidance.
4. Inspect `POST /api/incidents/{id}/recovery/dry-run` and verify `executed=false`.
5. Collect independent evidence and validate the proposed target. Do not treat mock diagnostics as a verified root cause.
6. Acknowledge or resolve the incident only after investigation; preserve actor, timestamp and notes.

## Runbook 2 — Review a recovery proposal

1. Start `POST /api/incidents/{id}/recovery/workflows`.
2. Review the diagnosis, evidence bundle, proposed action, safety checks and simulated effect.
3. Use `GET /api/recovery/approvals/{requestId}` to inspect pending state.
4. Approve or reject with a non-empty reason and a timestamp after submission.
5. Re-read the workflow and verify its terminal state. **Do not infer that approval caused a restart or scale-out**: this project cannot execute either.

## Runbook 3 — Local API troubleshooting

1. Verify the .NET 10 SDK or Docker runtime is installed.
2. Check the process is listening only on `127.0.0.1`.
3. Verify `IncidentDatabasePath` points to a writable location for the API user.
4. If Docker Compose fails, run `docker compose config` and `docker compose logs cloud-ops-api`.
5. Run both test projects to distinguish application defects from environment problems.
6. Do not delete a persisted SQLite database without first making a backup and confirming data-loss expectations.

## Runbook 4 — Safety stop / unexpected exposure

If the local API is accidentally exposed beyond localhost, immediately stop the container or API process, block inbound access, and review access logs where available. The API does **not** verify caller identity or tenant access. Do not use it with production secrets or customer data.

## Runbook 5 — Performance regression

1. Run the synthetic benchmark at a fixed sample count on the same machine and .NET runtime.
2. Record elapsed time, samples/second, commit SHA, CPU/memory conditions and run count.
3. Compare multiple runs rather than treating one measurement as representative.
4. Investigate changes in rule evaluation, allocations and ingestion bounds.
5. Do not extrapolate synthetic local throughput into a production SLA.
