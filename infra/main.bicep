// Safe, parameterized infrastructure skeleton for future deployment.
// This template declares no resources and incurs no Azure costs.
targetScope = 'resourceGroup'

@description('Logical application name used for future resources.')
@minLength(3)
@maxLength(40)
param appName string = 'autonomous-cloud-ops'

@description('Deployment location for future resources.')
param location string = resourceGroup().location

@description('Explicit safety gate. Live provisioning is not supported in this milestone.')
@allowed([
  false
])
param enableLiveResources bool = false

output deploymentMode string = 'template-only'
output applicationName string = appName
output targetLocation string = location
output resourcesProvisioned int = 0
