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

// The serving database. Locally compose creates it (POSTGRES_DB); in cloud it must be declared,
// or the migration step fails with "database does not exist".
resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: 'spansight'
  dependsOn: [allowedExtensions]
}

// Serialized after the configuration change and database creation: sibling child resources
// deploy in parallel by default, and an in-flight config update puts the server in 'Updating',
// which rejects Entra-admin operations (AadAuthOperationCannotBePerformedWhenServerIsNotAccessible
// — burned deploy runs 3-4). NOTE: principalName must be ≤63 chars — PostgreSQL truncates role
// identifiers to 63 bytes, and the administrators PUT is only idempotent when the submitted name
// matches the stored (truncated) one exactly.
resource entraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (entraAdminObjectId != '') {
  parent: server
  name: entraAdminObjectId
  properties: {
    principalType: 'User'
    principalName: entraAdminPrincipalName
    tenantId: subscription().tenantId
  }
  dependsOn: [database]
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
