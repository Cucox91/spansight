using 'main.bicep'

param env = 'demo'
param location = 'eastus2'

// TODO(raziel): set before the first Week 5 deployment — Postgres has password auth
// disabled (ADR-006-B: managed identity end-to-end), so an Entra admin is required.
param pgEntraAdminObjectId = ''
param pgEntraAdminPrincipalName = ''
