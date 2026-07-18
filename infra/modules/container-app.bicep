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

@description('Allowed CORS origin for the SPA (Static Web App hostname).')
param corsOrigin string

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
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            // Entra token auth: username is the ACA app name (the PG principal), no password.
            {
              name: 'ConnectionStrings__SpanSight'
              value: 'Host=${postgresFqdn};Port=5432;Database=spansight;Username=${name};Ssl Mode=Require'
            }
            { name: 'Database__UseEntraToken', value: 'true' }
            // Standard variable the Azure Monitor OTel distro reads (NFR-6)
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'Cors__Origins__0', value: corsOrigin }
          ]
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
