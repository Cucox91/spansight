// SpanSight demo environment — all-Azure per ADR-006-B (docs/ARCHITECTURE.md §5.2).
// Authored Week 1 as a reviewable baseline; nothing is deployed until Week 5's deploy.yml.
// Every Azure resource is provisioned from these files only (CLAUDE.md hard rule 5).
// Deploy (Week 5, from the pipeline): az deployment sub create --location <loc> \
//   --template-file infra/main.bicep --parameters infra/main.bicepparam
targetScope = 'subscription'

@description('Environment name used in resource names (single demo env per IMPLEMENTATION-PLAN §4).')
param env string = 'demo'

@description('Primary region for all resources (in-region API↔DB latency per ADR-006-B).')
param location string = 'eastus2'

@description('Object ID of the Entra principal that administers Postgres (password auth is disabled).')
param pgEntraAdminObjectId string = ''

@description('Display name / UPN of the Postgres Entra admin principal.')
param pgEntraAdminPrincipalName string = ''

var baseName = 'spansight'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${baseName}-${env}'
  location: location
}

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'log-analytics'
  scope: rg
  params: {
    name: 'log-${baseName}-${env}'
    location: location
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  scope: rg
  params: {
    name: 'psql-${baseName}-${env}'
    location: location
    entraAdminObjectId: pgEntraAdminObjectId
    entraAdminPrincipalName: pgEntraAdminPrincipalName
  }
}

module containerAppsEnv 'modules/container-apps-env.bicep' = {
  name: 'container-apps-env'
  scope: rg
  params: {
    name: 'cae-${baseName}-${env}'
    location: location
    logAnalyticsWorkspaceName: logAnalytics.outputs.workspaceName
  }
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'static-web-app'
  scope: rg
  params: {
    name: 'stapp-${baseName}-${env}'
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    // Storage account names: 3–24 chars, lowercase alphanumeric, globally unique.
    name: 'st${baseName}${env}'
    location: location
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'app-insights'
  scope: rg
  params: {
    name: 'appi-${baseName}-${env}'
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

output resourceGroupName string = rg.name
output postgresFqdn string = postgres.outputs.fqdn
output containerAppsEnvironmentId string = containerAppsEnv.outputs.environmentId
output staticWebAppDefaultHostname string = staticWebApp.outputs.defaultHostname
output storageAccountName string = storage.outputs.accountName
output appInsightsConnectionString string = appInsights.outputs.connectionString
