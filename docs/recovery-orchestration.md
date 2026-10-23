# Local recovery workflow orchestration (PR #15)

`RecoveryWorkflowService` connects the existing incident lifecycle, mock diagnostic provider, evidence retrieval, recovery planner, dry-run simulator and human approval ledger into a single local workflow.

## Workflow

1. Start from an existing incident ID.
2. Generate advisory diagnostics (offline mock).
3. Retrieve bounded, resource-scoped operational evidence.
4. Propose a recovery action and simulate it **without execution**.
5. Submit the action for human review.
6. Read approval state as `AwaitingApproval`, `Approved` or `Rejected`.

Repeated starts for the same incident return the same workflow and do not create additional approval requests. The service supports cancellation and never exposes a real recovery execution path.

## Local API

- `POST /api/incidents/{id}/recovery/workflows` starts or returns an existing workflow
- `GET /api/recovery/workflows` lists workflows
- `GET /api/recovery/workflows/{workflowId}` returns current workflow state

Approve or reject through the approval endpoints added in PR #14. Approval is advisory only: `RecoveryDryRunResult.Executed` always remains `false`.

## Boundaries and limitations

This PR is **not yet an Azure Durable Functions host**. The workflow runs in-process and uses in-memory state, so orchestration and approvals are lost on restart. Incident records themselves may still be stored in SQLite. A later Azure Functions adapter can schedule these steps as durable activities, persist orchestration state, and wait for external approval events.

The API remains unauthenticated, and reviewer names are caller-supplied. Bind locally only. The default evidence source is empty; provide sanitized evidence explicitly in tests or future adapters. No Azure subscriptions, paid APIs, external resources, or infrastructure changes are involved.
