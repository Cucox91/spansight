// SpanSight demo environment — all-Azure per ADR-006-B (docs/ARCHITECTURE.md §5.2).
// Authored Week 1 as a reviewable baseline; nothing is deployed until Week 5's deploy.yml.
// Every Azure resource is provisioned from these files only (CLAUDE.md hard rule 5).
// Deploy (Week 5, from the pipeline): az deployment sub create --location <loc> \
//   --template-file infra/main.bicep --parameters infra/main.bicepparam
targetScope = 'subscription'

@description('Environment name used in resource names (single demo env per IMPLEMENTATION-PLAN §4).')
param env string = 'demo'

@description('Primary region for all resources (in-region API↔DB latency per ADR-006-B).')
param location string = 'southcentralus'

@description('Static Web Apps is only offered in a short region list (centralus, eastus2, westus2, westeurope, eastasia); its assets are edge-served, so metadata placement does not affect latency.')
param swaLocation string = 'centralus'

@description('Object ID of the Entra principal that administers Postgres (password auth is disabled).')
param pgEntraAdminObjectId string = ''

@description('Display name / UPN of the Postgres Entra admin principal.')
param pgEntraAdminPrincipalName string = ''

@description('Email for the NFR-2 budget alert; armed with the very first deployment.')
param budgetContactEmail string

@description('API image (ghcr.io/...); empty on infra-only deploys — the app deploys once an image exists.')
param apiImage string = ''

@description('Ask the Map (FR-AI.1) feature flag — deploy passes the AI_ENABLED repo variable (RUNBOOK §9).')
param aiEnabled bool = false

@description('Anthropic API key from the ANTHROPIC_API_KEY GitHub secret. Empty = provider not registered (endpoint stays dark). @secure: never logged, never in deployment history.')
@secure()
param anthropicApiKey string = ''

@description('Custom subdomain hostnames for the SPA (e.g. www.spansights.com). Bound on the SWA; the matching CNAMEs live at the registrar (GoDaddy — RUNBOOK §8). API + storage CORS allowlists extend automatically via spaOrigins.')
param spaCustomDomains array = []

@description('Apex hostname for the SPA (e.g. spansights.com) — dns-txt-token validation + A record to the SWA stableInboundIP (RUNBOOK §8). Empty = none.')
param spaApexDomain string = ''

var baseName = 'spansight'

// Every origin the SPA is served from: default SWA hostname + custom subdomains + apex.
var spaCustomOrigins = [for domain in spaCustomDomains: 'https://${domain}']
var spaApexOrigins = spaApexDomain == '' ? [] : ['https://${spaApexDomain}']
var spaOrigins = concat(['https://${staticWebApp.outputs.defaultHostname}'], spaCustomOrigins, spaApexOrigins)

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${baseName}-${env}'
  location: location
}

// Subscription-scope budget: NFR-2 requires the alert armed before/with the first resource.
module budget 'modules/budget.bicep' = {
  name: 'budget'
  params: {
    contactEmail: budgetContactEmail
  }
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
    location: swaLocation
    customDomains: spaCustomDomains
    apexDomain: spaApexDomain
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    // Storage account names: 3–24 chars, lowercase alphanumeric, globally unique.
    name: 'st${baseName}${env}'
    location: location
    corsOrigins: spaOrigins
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

module containerApp 'modules/container-app.bicep' = if (apiImage != '') {
  name: 'container-app'
  scope: rg
  params: {
    name: 'ca-${baseName}-api-${env}'
    location: location
    environmentId: containerAppsEnv.outputs.environmentId
    image: apiImage
    postgresFqdn: postgres.outputs.fqdn
    appInsightsConnectionString: appInsights.outputs.connectionString
    corsOrigins: spaOrigins
    aiEnabled: aiEnabled
    anthropicApiKey: anthropicApiKey
  }
}

output resourceGroupName string = rg.name
output postgresFqdn string = postgres.outputs.fqdn
output containerAppsEnvironmentId string = containerAppsEnv.outputs.environmentId
output staticWebAppDefaultHostname string = staticWebApp.outputs.defaultHostname
output storageAccountName string = storage.outputs.accountName
output appInsightsConnectionString string = appInsights.outputs.connectionString
output apiFqdn string = apiImage != '' ? containerApp!.outputs.fqdn : ''
output apiPrincipalId string = apiImage != '' ? containerApp!.outputs.principalId : ''
