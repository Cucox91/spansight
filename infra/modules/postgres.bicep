@description('PostgreSQL Flexible Server B1ms + PostGIS — serving DB per ADR-001/ADR-006-B (~$17/mo).')
param name string
param location string
param entraAdminObjectId string
param entraAdminPrincipalName string

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  sku: {
    name: 'Standard_B1ms' // 1 vCPU / 2 GiB — cheapest tier, NFR-2
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    storage: {
      storageSizeGB: 32
      autoGrow: 'Disabled' // cost guard: fail loud rather than grow the bill
    }
    authConfig: {
      // Managed identity end-to-end (ADR-006-B): no password auth, no connection-string secrets.
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled' // demo tier; NFR-3 is best-effort
    }
  }
}

// Allowlist PostGIS so the Week-5 migration step can CREATE EXTENSION postgis.
resource allowedExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'azure.extensions'
  properties: {
    value: 'POSTGIS'
    source: 'user-override'
  }
}

resource entraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (entraAdminObjectId != '') {
  parent: server
  name: entraAdminObjectId
  properties: {
    principalType: 'User'
    principalName: entraAdminPrincipalName
    tenantId: subscription().tenantId
  }
}

// Public access + Azure-services firewall rule is the cheap demo pattern; VNet integration
// would force a costlier ACA workload profile. Review before Week 5 (data is public anyway).
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: server
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output fqdn string = server.properties.fullyQualifiedDomainName
