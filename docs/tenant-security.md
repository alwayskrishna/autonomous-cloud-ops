# Tenant isolation and authorization foundation (PR #17)

This milestone adds an **offline, core-library security foundation** for future authenticated, multi-tenant APIs. It is not a production security boundary.

## Components

- `TenantPrincipal` identifies a tenant, subject and role.
- `TenantAuthorizationPolicy` rejects cross-tenant access and enforces role permissions.
- `TenantIncidentStore` maintains separate incident lifecycle stores for each tenant. The same incident ID may exist in different tenants without collision.

| Role | Read incidents | Manage incidents | Request recovery | Decide recovery |
| --- | --- | --- | --- | --- |
| Reader | Yes | No | No | No |
| Operator | Yes | Yes | Yes | No |
| Approver | Yes | No | No | Yes |
| Administrator | Yes | Yes | Yes | Yes |

Example:

```csharp
var store = new TenantIncidentStore(new TenantAuthorizationPolicy());
var operatorPrincipal = new TenantPrincipal("tenant-a", "local-operator", TenantRole.Operator);
store.Open(operatorPrincipal, incident);
var reader = new TenantPrincipal("tenant-a", "local-reader", TenantRole.Reader);
var incidents = store.List(reader, "tenant-a");
```

## Critical limitations

- **Existing REST endpoints are still unauthenticated and single-tenant.** This PR does **not** secure those endpoints or retrofit the existing approval/workflow stores.
- The core API trusts a caller-supplied `TenantPrincipal`; there is no token validation, authenticated identity or tenant claim binding. Never construct a principal directly from untrusted request headers or bodies.
- Tenant incident stores are in-memory and are not connected to SQLite persistence.
- Recovery authorization permissions are policy definitions only; recovery workflows and approvals do not yet invoke them.
- No security claims should be made for public deployment. A later integration must add real authentication, mandatory authorization on **every** API route, tenant-aware persistence and audit trails, and adversarial cross-tenant integration tests before public exposure.

All tests run offline without Azure credentials, resources or paid APIs.
