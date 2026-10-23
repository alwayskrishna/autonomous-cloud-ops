# Azure Monitor Metrics REST reader (PR #26)

`AzureMonitorRestMetricReader` is an opt-in implementation of `IAzureMetricReader`. It calls the Azure Resource Manager Metrics REST endpoint with an injected bearer token from `IAzureManagementTokenProvider`. The reader does not acquire credentials itself, persist tokens, or make network calls until explicitly invoked.

The caller must provide an Azure ARM resource ID, an `HttpClient`, and a trusted token provider (prefer managed identity for Azure-hosted deployments). Metrics queried: `Percentage CPU`, `Memory Percentage`, `Http 5xx`. **Metric availability and aggregation semantics vary by resource type**; this adapter is not guaranteed to work with every Azure service. Missing datapoints are skipped.

The reader is not registered in the API, and no Azure account, resource or paid service is provisioned. Live Azure Monitor/Log Analytics integration and end-to-end credential wiring remain incomplete.
