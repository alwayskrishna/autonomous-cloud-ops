# AKS and Azure Container Apps recovery preview adapters (PR #16)

This milestone adds **offline, read-only** platform-specific recovery previews.

- `AksRecoveryPreviewAdapter` describes a Kubernetes deployment restart, replica scaling, or investigation using an explicit cluster resource, namespace and workload name.
- `ContainerAppsRecoveryPreviewAdapter` describes revision restart evaluation, maximum-replica configuration proposals, or investigation using an explicit Azure resource.
- `PlatformRecoveryPreviewService` dispatches to the appropriate adapter by platform.

Both adapters validate action contracts, target identifiers, and exact resource-name matching. Every `PlatformRecoveryPreview.Executed` value is **false**. No Kubernetes client, Azure SDK, shell commands, credentials, or HTTP calls are used.

## Local API

`POST /api/recovery/platform-preview`

Example request:

```json
{
  "action": {
    "id": "action-1",
    "incidentId": "incident-1",
    "resourceId": "service-a",
    "kind": 1,
    "rationale": "High CPU",
    "targetInstanceCount": 3
  },
  "target": {
    "platform": 0,
    "subscriptionId": "demo-subscription",
    "resourceGroup": "demo-rg",
    "resourceName": "service-a",
    "namespace": "default",
    "workloadName": "frontend"
  }
}
```

Here `kind=1` means `ScaleOut` and `platform=0` means `Aks`. The response describes what **would** be proposed, not an operation to execute.

## Production limitations

These are descriptive previews, not live cluster simulations or reliable impact forecasts. No resource discovery, policy enforcement, authorization, approval binding, disruption budget checks, cost calculation or rollback is implemented. Azure Container Apps maximum replicas are a scaling ceiling, not a guaranteed number of running replicas.

The API is a local, unauthenticated prototype and must not be deployed publicly. Later milestones must add identity, tenant isolation, durable audit, policy checks and safe execution controls before real adapters could be considered.
