@description('SpanSight API on Container Apps — scale-to-zero-capable demo tier (ADR-006-B).')
param name string
param location string
param environmentId string

@description('Full image reference, e.g. ghcr.io/cucox91/spansight-api:sha-abc123.')
param image string

@description('Postgres FQDN for the Entra-token connection (no password — ADR-006-B).')
param postgresFqdn string

@description('App Insights connection string (OTel exporter).')
param appInsightsConnectionString string

@description('Allowed CORS origins for the SPA (SWA default hostname + any custom domains).')
param corsOrigins array

@description('Ask the Map (FR-AI.1) feature flag — deploy passes the AI_ENABLED repo variable (RUNBOOK §9).')
param aiEnabled bool = false

@description('Anthropic API key from the ANTHROPIC_API_KEY GitHub secret. Empty = provider not registered (endpoint stays dark). @secure: never logged, never in deployment history.')
@secure()
param anthropicApiKey string = ''

// Program.cs binds Cors:Origins as string[] — emit one indexed env var per origin.
var corsEnv = [for (origin, i) in corsOrigins: { name: 'Cors__Origins__${i}', value: origin }]

// FR-AI.1 (RUNBOOK §9): the ACA secret only exists once a key is supplied, so an
// infra-only deploy declares no secret and the endpoint keeps its dark 503 path.
var aiSecrets = anthropicApiKey == '' ? [] : [{ name: 'anthropic-api-key', value: anthropicApiKey }]
var aiEnv = concat(
  [
    { name: 'Ai__Enabled', value: aiEnabled ? 'true' : 'false' }
    { name: 'Ai__Provider', value: 'anthropic' }
    { name: 'Ai__Model', value: 'claude-haiku-4-5' } // ADR-008 implementation pin
  ],
  anthropicApiKey == '' ? [] : [{ name: 'ANTHROPIC_API_KEY', secretRef: 'anthropic-api-key' }]
)

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned' // DB principal created once from SETUP-AZURE.md §4
  }
  properties: {
    environmentId: environmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
      }
      secrets: aiSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat([
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            // Entra token auth: username is the ACA app name (the PG principal), no password.
            {
              name: 'ConnectionStrings__SpanSight'
              value: 'Host=${postgresFqdn};Port=5432;Database=spansight;Username=${name};Ssl Mode=Require'
            }
            { name: 'Database__UseEntraToken', value: 'true' }
            // Standard variable the Azure Monitor OTel distro reads (NFR-6)
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
          ], corsEnv, aiEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/readyz', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 6
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0 // scale to zero off-hours; cold start is acceptable for a demo (NFR-2)
        maxReplicas: 1
      }
    }
  }
}

output fqdn string = app.properties.configuration.ingress.fqdn
output principalId string = app.identity.principalId
