# Persistent incident storage (PR #8)

The incident lifecycle store now supports an optional SQLite database path. Opening, acknowledging and resolving an incident persist a serialized snapshot, including its complete transition history, before updating the in-memory cache. Existing callers that construct `new IncidentLifecycleStore()` continue using the original ephemeral in-memory behavior.

The local management API defaults to `data/incidents.db` under its content root. Override the location with the `IncidentDatabasePath` configuration key, for example:

```bash
IncidentDatabasePath=/tmp/cloudops-incidents.db dotnet run --project src/AutonomousCloudOps.Api --urls http://127.0.0.1:5080
```

The API's `Testing` environment uses isolated in-memory storage to avoid cross-test state contamination. The database is created on first use. Existing data is loaded at startup, and lifecycle state and history survive restarts.

## Limitations

- This is a **single-process prototype**. Concurrent API processes pointing to the same file do not coordinate their in-memory caches.
- Records are persisted as JSON snapshots in a SQLite table; schema migration, indexing, query pagination and retention policies are future work.
- SQLite write errors fail the operation instead of silently losing data.
- The API remains unauthenticated and must bind only to loopback for local testing. Do not deploy publicly.
- No Azure database or paid resources are provisioned.
