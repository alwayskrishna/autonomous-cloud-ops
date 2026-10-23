# Tenant-scoped API access (PR #22)

New routes: `GET /api/tenants/{tenantId}/incidents`, `GET /api/tenants/{tenantId}/incidents/{id}`, and `POST /api/tenants/{tenantId}/incidents`. Tenant identity comes from the **validated JWT** `tid` claim, user identity from `oid` or `sub`, and role from the `roles` claim. Caller-supplied tenant route IDs must exactly match the token tenant. Roles: Reader, Operator, Approver, Administrator.

**Fail-closed migration:** outside the `Testing` environment, the legacy `/api/*` endpoints are blocked with HTTP 403 because their singleton stores do not isolate tenants. The new tenant routes use an in-memory tenant-keyed incident store. This means recovery and approval APIs are intentionally **unavailable** in non-test environments until they can be migrated safely.

Identity claims must be issued by a trusted configured authority. No authorization decisions rely on arbitrary request headers. The `Testing` environment continues to support legacy integration tests and must never be used for deployment. Tenant store is in-memory and is not durable. Further work is needed before production.
