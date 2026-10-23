# Human recovery approval workflow (PR #14)

`RecoveryApprovalStore` records proposed recovery actions and enforces a single, terminal decision:

`Pending → Approved` or `Pending → Rejected`.

Each decision captures the actor name, timestamp and mandatory reason. Invalid transitions, repeated decisions, timestamps before submission, and invalid recovery action contracts are rejected. Concurrent decisions are synchronized.

Local API routes:

- `POST /api/incidents/{id}/recovery/approvals` — submit a suggested action for review
- `GET /api/recovery/approvals` — list approvals
- `GET /api/recovery/approvals/{requestId}` — retrieve approval and decision history
- `POST /api/recovery/approvals/{requestId}/approve` — approve
- `POST /api/recovery/approvals/{requestId}/reject` — reject

Example decision JSON:

```json
{"actor":"local-reviewer","at":"2026-10-23T18:00:00Z","reason":"Validated resource ownership and blast radius"}
```

**Important limitations:** This is a **local, in-memory prototype**. Approval records do not yet survive API restart. The `actor` field is caller-supplied and is **not an authenticated identity**. The API has no authorization and must never be exposed publicly. Approval does **not** execute actions, and there is no infrastructure execution capability. Production use requires durable approval storage, identity verification, RBAC, tamper-resistant audit logs, and independent policy checks.
