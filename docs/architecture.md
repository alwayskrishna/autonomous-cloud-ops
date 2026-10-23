# Architecture and design decisions

## Components and data flow

1. `ITelemetrySource` supplies samples. The ingestion service bounds sample counts and evaluates configurable alert rules.
2. `IncidentCorrelator` groups repeated detections; `IncidentLifecycleStore` tracks state transitions and can persist incident records to SQLite.
3. `IncidentDiagnosticService` accepts a provider interface. The current mock generates deterministic, advisory guidance; `IncidentEvidenceService` fetches bounded, resource-scoped evidence.
4. `RecoveryWorkflowService` assembles diagnostics, evidence, action planning, dry-run output and approval tracking.
5. `RecoveryApprovalStore` enforces terminal approve/reject decisions. `PlatformRecoveryPreviewService` produces descriptive AKS or Container Apps proposals. Neither has an execution method.

## Key decisions

**Offline-first:** provider interfaces and in-memory mocks keep CI reproducible and cloud costs at zero.

**Human-governed, non-executing recovery:** planning, simulation and approval are separate. A recorded approval does not authorize a mutation.

**Explicit resource identity:** platform preview adapters require matching action and target resource names. This is not a substitute for production resource IDs or tenant-scoped authorization.

**Bounded processing:** ingestion has a maximum sample count, evidence queries have time windows and record limits, and retry attempts are bounded.

**Persistence boundary:** incident lifecycle supports SQLite. Approval and workflow state remain process-local and disappear after restart.

**Security boundary:** tenant-aware authorization exists as a separate core prototype. The existing REST API is not authenticated, does not enforce tenant-scoped access, and must stay on localhost.

## Future architecture work

Production deployment would require verified identity and tenant claims, authorization on all routes, durable workflow/approval state, idempotent distributed activity scheduling, real MCP protocol support, secrets management, telemetry exporters, rate limits, workload policy checks, and rollback procedures. Azure Durable Functions and cloud recovery adapters have **not** been implemented.
