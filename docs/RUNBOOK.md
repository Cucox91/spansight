# SpanSight — Runbook

v1.0 · 2026-07-18 · Release, operations, and data procedures for the `demo` environment (ADR-006-B). Companion to [SDLC.md](./SDLC.md) · [IMPLEMENTATION-PLAN.md](./IMPLEMENTATION-PLAN.md).

Credential/billing steps are marked **[RAZIEL]** — they stay human under AI-USAGE v1.2 and are never executed by AI or stored in the repo.

## 1. One-time Azure setup (before the first deploy)

### 1.1 Subscription **[RAZIEL]**

1. Upgrade the trial subscription to Pay-As-You-Go before it lapses (portal → Subscription → Upgrade).
2. Note the subscription id and tenant id: `az account show --query '{sub:id, tenant:tenantId}'`.

### 1.2 Deployment identity + OIDC federation **[RAZIEL]**

No stored cloud credentials — GitHub Actions federates directly (ARCHITECTURE §7). Display name **must** be `spansight-deploy` (it doubles as the Postgres principal name in §1.4):

```bash
az ad app create --display-name spansight-deploy
APP_ID=$(az ad app list --display-name spansight-deploy --query '[0].appId' -o tsv)
az ad sp create --id "$APP_ID"

# Federated credential for this repo's main branch
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "spansight-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:Cucox91/spansight:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Contributor on the subscription (creates the RG + resources; budgets need subscription scope)
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$(az account show --query id -o tsv)"
```

### 1.3 GitHub secrets & variables **[RAZIEL]**

Repo → Settings → Secrets and variables → Actions:

| Kind | Name | Value |
|---|---|---|
| Secret | `AZURE_CLIENT_ID` | `$APP_ID` from §1.2 |
| Secret | `AZURE_TENANT_ID` | tenant id |
| Secret | `AZURE_SUBSCRIPTION_ID` | subscription id |
| Variable | `BUDGET_ALERT_EMAIL` | alert recipient (NFR-2) |
| Variable | `VITE_TILES_URL` | *(set after §3; empty until then)* |

Also set `pgEntraAdminObjectId` / `pgEntraAdminPrincipalName` in `infra/main.bicepparam` (your user: `az ad signed-in-user show --query '{id:id, upn:userPrincipalName}'`) — by PR, like any infra change.

### 1.4 Postgres principals (after the first `deploy` run provisions the server) **[RAZIEL]**

Password auth is disabled (ADR-006-B); create the two Entra principals once, connected as the Entra admin from §1.3:

```bash
export PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
psql "host=psql-spansight-demo.postgres.database.azure.com dbname=postgres user=<your-upn> sslmode=require" <<'SQL'
SELECT * FROM pgaadauth_create_principal('spansight-deploy', false, false);      -- migrations (DDL)
SELECT * FROM pgaadauth_create_principal('ca-spansight-api-demo', false, false); -- API (read-only)
SQL
psql "host=psql-spansight-demo.postgres.database.azure.com dbname=spansight user=<your-upn> sslmode=require" <<'SQL'
GRANT CREATE ON DATABASE spansight TO "spansight-deploy";
SQL
```

After the first migration has created the schemas, grant the API its read-only surface (least privilege — the API never writes; ingestion does):

```sql
GRANT USAGE ON SCHEMA core, ops, quarantine TO "ca-spansight-api-demo";
GRANT SELECT ON ALL TABLES IN SCHEMA core, ops, quarantine TO "ca-spansight-api-demo";
ALTER DEFAULT PRIVILEGES FOR ROLE "spansight-deploy" IN SCHEMA core, ops, quarantine
  GRANT SELECT ON TABLES TO "ca-spansight-api-demo";
```

*(Adjust schema list if `\dn` shows different names — the migration is the source of truth.)*

## 2. Deploying

Actions → **Deploy** → Run workflow (`main`). The run: builds/pushes the API image to GHCR → `az deployment sub create` over `infra/` (budget alert deploys with everything else) → EF migration with an Entra token through a transient runner firewall rule (removed in the same run) → SPA build against the deployed API origin → SWA publish → readiness/reachability smoke. First green run: flip the trigger to include `push: branches: [main]` by PR.

## 3. Data load + tiles (dev Mac → cloud, after §1.4)

Ingestion never runs in the cloud (ARCHITECTURE §3) — load the snapshot from the dev Mac, then publish tiles:

```bash
# Temporary client-IP firewall rule (remove after — operational, not infrastructure)
az postgres flexible-server firewall-rule create -g rg-spansight-demo -s psql-spansight-demo \
  --name dev-mac --start-ip-address "$(curl -fsS https://api.ipify.org)" --end-ip-address "$(curl -fsS https://api.ipify.org)"

TOKEN=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
dotnet run -c Release --project src/SpanSight.Ingestion -- load \
  --file data/2025AllRecordsDelimitedAllStates.txt --snapshot-year 2025 \
  --connection "Host=psql-spansight-demo.postgres.database.azure.com;Database=spansight;Username=<your-upn>;Password=$TOKEN;Ssl Mode=Require"

tools/build-tiles.sh --connection "<same connection string>"   # or reuse the local build if the run ids match
az storage blob upload --account-name stspansightdemo --container-name '$web' \
  --name tiles/bridges.pmtiles --file data/tiles/bridges.pmtiles --overwrite --auth-mode login
az storage blob upload --account-name stspansightdemo --container-name '$web' \
  --name tiles/manifest.json --file data/tiles/manifest.json --overwrite --auth-mode login

az postgres flexible-server firewall-rule delete -g rg-spansight-demo -s psql-spansight-demo --name dev-mac --yes
```

Then set the `VITE_TILES_URL` repo variable to the blob URL and re-run **Deploy** so the SPA switches from the GeoJSON fallback to vector tiles (FR-0.5 AC-2). Re-run with `run_e2e: true` for the full live smoke.

## 4. Rollback

Images are immutable (`sha-<commit>` tags). Re-run **Deploy** from the last good commit (Actions → Deploy → choose the ref), or roll just the API back:

```bash
az containerapp update -g rg-spansight-demo -n ca-spansight-api-demo \
  --image ghcr.io/cucox91/spansight-api:sha-<last-good>
```

Bicep is idempotent — re-deploying a good commit converges infrastructure. The DB rolls forward only (EF migrations are additive in Phase 0; destructive changes need an ADR + explicit plan).

## 5. Operations

- **Cost (NFR-2):** budget `budget-spansight` alerts at $40 actual / $50 forecast to `BUDGET_ALERT_EMAIL`; spend reviewed at every phase gate (SDLC §3.6). Biggest levers: PG Flexible B1ms (~$17/mo), ACA scale-to-zero, Free SWA.
- **Observability (NFR-6):** App Insights via the Azure Monitor OTel distro (`APPLICATIONINSIGHTS_CONNECTION_STRING` set by Bicep). Verify one browser → API → DB trace in App Insights at the Week-5 exit.
- **Health:** `/healthz` (liveness), `/readyz` (DB round-trip) — probed by Container Apps and the deploy smoke.
- **Annual refresh (FR-3.4, future):** §3 rerun with the new snapshot year; idempotency keys on file SHA-256.

## 6. Swiftly / Phase 2 note

The GTFS-RT key stays in the password manager until Phase 2 wiring; it enters `.env` locally and GitHub secrets only (NFR-8 §10). Cached Swiftly-derived data is deleted on termination (§14).
