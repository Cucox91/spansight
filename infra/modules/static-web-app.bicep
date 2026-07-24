@description('Static Web App (Free) for the SPA — Standard (+$9, SLA) is a deliberate later decision (§6 carry-over).')
param name string
param location string

@description('Custom subdomain hostnames served by the SWA (e.g. www.spansights.com). DNS-first: each hostname\'s registrar CNAME → defaultHostname must resolve publicly BEFORE this deploys — validation is cname-delegation and the deployment blocks on it (RUNBOOK §8). Free tier includes 2 custom domains; certificates are issued and renewed automatically.')
param customDomains array = []

@description('Apex hostname (e.g. spansights.com), served directly by the SWA over its stableInboundIP A record. Validation is dns-txt-token: the first deploy generates the token (fetch with az staticwebapp hostname show, place the TXT at the registrar while the deployment waits — RUNBOOK §8). Empty = no apex binding.')
param apexDomain string = ''

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

// Subdomains: cname-delegation (the default validation) against the registrar CNAME.
resource customDomain 'Microsoft.Web/staticSites/customDomains@2024-04-01' = [
  for domain in customDomains: {
    parent: staticWebApp
    name: domain
    properties: {}
  }
]

// Apex: dns-txt-token validation + registrar A record to stableInboundIP (GoDaddy has no
// ALIAS/ANAME, so this is the full-HTTPS apex path — single regional host, accepted for a
// demo; MS's edge-distributed alternative needs an ALIAS-capable DNS host). RUNBOOK §8.
resource apexCustomDomain 'Microsoft.Web/staticSites/customDomains@2024-04-01' = if (apexDomain != '') {
  parent: staticWebApp
  name: apexDomain
  properties: {
    validationMethod: 'dns-txt-token'
  }
}

output defaultHostname string = staticWebApp.properties.defaultHostname
