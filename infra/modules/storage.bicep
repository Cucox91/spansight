@description('Blob storage: public PMTiles (hot) + private Parquet archive (cool) per ADR-005/ADR-006-B.')
param name string
param location string

@description('SPA origin allowed to make cross-origin range requests for PMTiles; empty disables CORS (e.g. before the SWA exists).')
param corsOrigin string = ''

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Anonymous read is enabled solely for the tiles container below: PMTiles are served as
    // public static assets via HTTP range requests (ADR-002). The data is public domain.
    allowBlobPublicAccess: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    // MapLibre's pmtiles protocol reads the archive with cross-origin HTTP range requests,
    // so the SPA origin needs Range allowed and Content-Range/ETag exposed (ADR-002).
    cors: corsOrigin == '' ? null : {
      corsRules: [
        {
          allowedOrigins: [corsOrigin]
          allowedMethods: ['GET', 'HEAD', 'OPTIONS']
          allowedHeaders: ['Range', 'If-Match', 'If-None-Match']
          exposedHeaders: ['Content-Range', 'Content-Length', 'Accept-Ranges', 'ETag']
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

resource tilesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'tiles'
  properties: {
    publicAccess: 'Blob'
  }
}

resource parquetArchiveContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'parquet-archive'
  properties: {
    publicAccess: 'None'
  }
}

// Cool tier for the rarely-read Parquet archive (< $1/mo total per §5.2).
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'parquet-archive-to-cool'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: ['blockBlob']
              prefixMatch: ['parquet-archive/']
            }
            actions: {
              baseBlob: {
                tierToCool: {
                  daysAfterModificationGreaterThan: 0
                }
              }
            }
          }
        }
      ]
    }
  }
}

output accountName string = account.name
