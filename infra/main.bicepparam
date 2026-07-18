using 'main.bicep'

param env = 'demo'
param location = 'eastus2'

// TODO(raziel): set before the first Week 5 deployment — Postgres has password auth
// disabled (ADR-006-B: managed identity end-to-end), so an Entra admin is required.
param pgEntraAdminObjectId = ''
param pgEntraAdminPrincipalName = ''

// Overridden per-deploy from workflow inputs (docs/SETUP-AZURE.md):
// budget alert recipient (NFR-2) and the GHCR image for the API container.
param budgetContactEmail = readEnvironmentVariable('BUDGET_CONTACT_EMAIL', '')
param apiImage = readEnvironmentVariable('API_IMAGE', '')
