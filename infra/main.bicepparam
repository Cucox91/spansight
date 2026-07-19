using 'main.bicep'

param env = 'demo'
param location = 'southcentralus'

// Postgres Entra admin (password auth is disabled — ADR-006-B). Raziel's user principal;
// identifiers, not secrets. The name is the full UPN TRUNCATED TO 63 CHARS — PostgreSQL's
// identifier limit; Azure stores the truncated form and the administrators PUT is only
// idempotent when this matches it exactly (see postgres.bicep).
param pgEntraAdminObjectId = '7fe0d49a-907d-4ba1-b632-e52aa5f186fa'
param pgEntraAdminPrincipalName = 'raziel.arias1991_outlook.com#EXT#@razielarias1991outlook.onmicr'

// Overridden per-deploy from workflow inputs (docs/SETUP-AZURE.md):
// budget alert recipient (NFR-2) and the GHCR image for the API container.
param budgetContactEmail = readEnvironmentVariable('BUDGET_CONTACT_EMAIL', '')
param apiImage = readEnvironmentVariable('API_IMAGE', '')
