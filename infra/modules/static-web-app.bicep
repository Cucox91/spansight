@description('Static Web App (Free) for the SPA — Standard (+$9, SLA) is a deliberate later decision (§6 carry-over).')
param name string
param location string

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: name
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // No repository binding: deploys use the SWA deployment token from GitHub Actions (Week 5),
    // keeping the resource definition independent of the repo.
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Enabled'
  }
}

output defaultHostname string = staticWebApp.properties.defaultHostname
