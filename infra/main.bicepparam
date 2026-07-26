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

// Custom domain (2026-07-24, RUNBOOK §8): DNS at GoDaddy. www CNAMEs to the SWA
// (cname-delegation — the CNAME must resolve before this deploys); the apex is a real SWA
// hostname too (dns-txt-token + A record to stableInboundIP), so https://spansights.com
// serves with its own cert. Canonical URL stays https://www.spansights.com.
param spaCustomDomains = ['www.spansights.com']
param spaApexDomain = 'spansights.com'

// Ask the Map (FR-AI.1) — RUNBOOK §9. Both default safe: missing var/secret ⇒ dark.
param aiEnabled = readEnvironmentVariable('AI_ENABLED', 'false') == 'true'
param anthropicApiKey = readEnvironmentVariable('ANTHROPIC_API_KEY', '')
