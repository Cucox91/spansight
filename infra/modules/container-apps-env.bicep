@description('Container Apps consumption environment — API/SignalR/poller/Redis sidecar land here in Week 5.')
param name string
param location string
param logAnalyticsWorkspaceName string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    // No workload profiles: consumption-only, scale-to-zero ($0 in Phases 0–1 per ADR-006-B).
  }
}

output environmentId string = environment.id
