# SpanSight — Runbook

v1.3 · 2026-07-24 · Release, operations, and data procedures for the `demo` environment (ADR-006-B). Companion to [SDLC.md](./SDLC.md) · [IMPLEMENTATION-PLAN.md](./IMPLEMENTATION-PLAN.md). v1.1 folds in everything the first live setup taught us (§7); v1.2 adds the custom domain (§8); v1.3 adds the Phase 0.5 AI-flip procedure (§9).

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

Password auth is disabled (ADR-006-B); create the two Entra principals once, connected as the Entra admin from §1.3.

> **63-byte identifiers:** PostgreSQL truncates role names at 63 bytes. A long UPN (guest accounts especially) is stored truncated, and psql logins must use the *truncated* form exactly — the same form pinned as `pgEntraAdminPrincipalName` in `infra/main.bicepparam`, where it also keeps the Bicep `administrators` PUT idempotent.

```bash
export PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
psql "host=psql-spansight-demo.postgres.database.azure.com dbname=postgres user=<your-upn-truncated-to-63> sslmode=require" <<'SQL'
SELECT * FROM pgaadauth_create_principal('spansight-deploy', false, false);      -- migrations (DDL)
SELECT * FROM pgaadauth_create_principal('ca-spansight-api-demo', false, false); -- API (read-only)
SQL
psql "host=psql-spansight-demo.postgres.database.azure.com dbname=spansight user=<your-upn-truncated-to-63> sslmode=require" <<'SQL'
GRANT CREATE ON DATABASE spansight TO "spansight-deploy";
-- PG 15+ locks the public schema; EF's __EFMigrationsHistory lives there.
GRANT USAGE, CREATE ON SCHEMA public TO "spansight-deploy";
-- PostGIS is an untrusted extension on flexible server: the migration's
-- CREATE EXTENSION IF NOT EXISTS fails for non-members even when the
-- extension already exists (the statement itself is gated).
GRANT azure_pg_admin TO "spansight-deploy";
SQL
```

After the first migration has created the schemas, grant the API its read-only surface (least privilege — the API never writes; ingestion does):

```sql
GRANT USAGE ON SCHEMA core, ops, quarantine TO "ca-spansight-api-demo";
GRANT SELECT ON ALL TABLES IN SCHEMA core, ops, quarantine TO "ca-spansight-api-demo";
ALTER DEFAULT PRIVILEGES FOR ROLE "spansight-deploy" IN SCHEMA core, ops, quarantine
  GRANT SELECT ON TABLES TO "ca-spansight-api-demo";
```

*(Adjust schema list if `\dn` shows different names — the migration is the source of truth. `ALTER DEFAULT PRIVILEGES FOR ROLE` needs membership in `spansight-deploy`; `GRANT "spansight-deploy" TO CURRENT_USER;` first if it complains.)*

## 2. Deploying

Every merge to `main` deploys automatically (trigger flipped after the first green run, 2026-07-19); Actions → **Deploy** → Run workflow for the `run_e2e` option. The run: builds/pushes the API image to GHCR → `az deployment sub create` over `infra/` (budget alert deploys with everything else) → EF migration with an Entra token through a transient runner firewall rule (removed in the same run) → SPA build against the deployed API origin → SWA publish → readiness/reachability smoke.

## 3. Data load + tiles (dev Mac → cloud, after §1.4)

Ingestion never runs in the cloud (ARCHITECTURE §3) — load the snapshot from the dev Mac, then publish tiles:

```bash
# Temporary client-IP firewall rule (remove after — operational, not infrastructure)
az postgres flexible-server firewall-rule create -g rg-spansight-demo -s psql-spansight-demo \
  --name dev-mac --start-ip-address "$(curl -fsS https://api.ipify.org)" --end-ip-address "$(curl -fsS https://api.ipify.org)"

TOKEN=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
# Command Timeout=300: B1ms over WAN occasionally exceeds Npgsql's default 30 s on
# large upsert batches (the 2026-07-19 load died at 250k rows without it).
dotnet run -c Release --project src/SpanSight.Ingestion -- load \
  --file data/2025AllRecordsDelimitedAllStates.txt --snapshot-year 2025 \
  --connection "Host=psql-spansight-demo.postgres.database.azure.com;Database=spansight;Username=<your-upn-truncated-to-63>;Password=$TOKEN;Ssl Mode=Require;Command Timeout=300"

tools/build-tiles.sh --connection "<same connection string>"   # or reuse the local build if the run ids match
# --auth-mode key: ARM Owner lacks data-plane blob RBAC; the CLI fetches the account
# key via ARM internally (grant yourself Storage Blob Data Contributor to use login).
az storage blob upload --account-name stspansightdemo --container-name tiles \
  --name bridges.pmtiles --file data/tiles/bridges.pmtiles --overwrite --auth-mode key
az storage blob upload --account-name stspansightdemo --container-name tiles \
  --name manifest.json --file data/tiles/manifest.json --overwrite --auth-mode key

az postgres flexible-server firewall-rule delete -g rg-spansight-demo --server-name psql-spansight-demo --name dev-mac --yes
```

Then set the `VITE_TILES_URL` repo variable to the bare blob URL (`https://stspansightdemo.blob.core.windows.net/tiles/bridges.pmtiles` — the SPA adds the `pmtiles://` prefix) and let the next deploy switch the SPA from the GeoJSON fallback to vector tiles (FR-0.5 AC-2); blob CORS for the SWA origin's range requests is declared in `infra/modules/storage.bicep`. Dispatch **Deploy** with `run_e2e: true` for the full live smoke.

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

## 7. Setup log — how it actually ran (2026-07-19)

Nine Deploy runs to first green; each failure was one layer deeper. Kept as the study trail for the sections above:

| Run | Failure | Fix |
|---|---|---|
| 1 | SWA not offered in `southcentralus` | `swaLocation='centralus'` param — SWA is edge-served, placement is metadata (PR #10) |
| 2 | PG `LocationIsOfferRestricted` | Subscription was still Free Trial → PAYG upgrade (§1.1) |
| 3–4 | `AadAuthOperationCannotBePerformedWhenServerIsNotAccessible`; Entra-admin PUT non-idempotent | ARM deploys sibling children in parallel → serialized config → database → admin in `postgres.bicep`; pinned the 63-byte-truncated UPN in `main.bicepparam` (PR #11) |
| 5 | Firewall step: az CLI renamed flags | `--server-name` / `--name` (PR #12) |
| 6 | Migration `42501: permission denied for schema public` | `GRANT USAGE, CREATE ON SCHEMA public` (§1.4) |
| 7–8 | `CREATE EXTENSION postgis` refused — untrusted extension, gated even with `IF NOT EXISTS` on an existing extension | `GRANT azure_pg_admin TO "spansight-deploy"` (§1.4) |
| 9 | — | Green end to end; demo live |

Post-green: national load required `Command Timeout=300` (§3); tile upload required `--auth-mode key` (§3); blob CORS for PMTiles range requests landed as Bicep (PR #13); deploy-on-main trigger flipped (PR #14). Permission classifier kept the role-escalation grant (`azure_pg_admin`) human-executed, consistent with AI-USAGE v1.2 boundaries.

## 8. Custom domain — spansights.com (added 2026-07-24)

Canonical URL: **https://www.spansights.com**; the apex `https://spansights.com` serves too, with its own cert. DNS stays at GoDaddy; the SWA hostname bindings and both CORS allowlists (API `Cors__Origins__N`, storage PMTiles rules) are declared in `infra/` — `spaCustomDomains` + `spaApexDomain` in `main.bicepparam` — never in the portal (hard rule 5).

Two validation mechanics, hence the ordering below: **www** is `cname-delegation` (its CNAME must resolve publicly *before* the deploy, which blocks on it) · **apex** is `dns-txt-token` (the token doesn't exist until a deploy first registers the hostname, so its TXT record goes in *during* the deploy). GoDaddy has no ALIAS/ANAME, so the apex uses an A record to the SWA's `stableInboundIP` — single regional host rather than the global edge (MS's documented trade-off); accepted for a demo, and why `www` stays canonical. Free tier includes 2 custom domains — www + apex uses exactly the allowance. Do **not** use GoDaddy domain forwarding for the apex — it would fight the A record, and its forwarding host can't serve HTTPS.

### 8.1 Before the deploy **[RAZIEL]**

First make sure the CLI is on the SpanSight subscription — other projects' `az account set`/`az login` calls move it, and every command below then fails with `ResourceGroupNotFound` (bit us 2026-07-24):

```bash
az account show -o table            # wrong subscription? → az account list -o table && az account set --subscription "<id>"
```

Get the SWA's stable inbound IP:

```bash
az staticwebapp show -n stapp-spansight-demo -g rg-spansight-demo -o json | grep -i stableinbound
```

GoDaddy → My Products → `spansights.com` → **DNS** → add (and make sure Forwarding is OFF for this domain — it parks its own apex A records):

| Type | Name | Value | TTL |
|---|---|---|---|
| CNAME | `www` | `kind-river-0d5c2f510.7.azurestaticapps.net` | default |
| A | `@` | `<stableInboundIP from the command above>` | default |

Verify the CNAME resolves before merging (the A record has no deadline):

```bash
dig +short www.spansights.com CNAME    # → kind-river-0d5c2f510.7.azurestaticapps.net.
```

### 8.2 Deploy + apex TXT **[RAZIEL]**

Merge the PR (or Actions → **Deploy** → Run workflow). While the `static-web-app` deployment step is running/waiting, fetch the apex validation token and add it at GoDaddy:

```bash
az staticwebapp hostname show -n stapp-spansight-demo -g rg-spansight-demo \
  --hostname spansights.com --query validationToken -o tsv
```

| Type | Name | Value | TTL |
|---|---|---|---|
| TXT | `@` | `<validationToken>` | default |

Validation completes in-run if the TXT propagates within the deployment's wait; if the run times out first, leave the records in place and re-run **Deploy** — the token is stable and the re-run converges (Bicep is idempotent). Certificates for both hostnames issue automatically within ~15 min of validation.

### 8.3 Verify

```bash
curl -sI https://www.spansights.com | head -1     # HTTP/2 200, valid cert
curl -sI https://spansights.com | head -1         # HTTP/2 200, valid cert (apex)
# API CORS carries both new origins (simple-request check):
for o in https://www.spansights.com https://spansights.com; do
  curl -sI -H "Origin: $o" \
    https://ca-spansight-api-demo.wonderfulforest-bd8cc0ce.southcentralus.azurecontainerapps.io/api/stats/summary \
    | grep -i access-control-allow-origin
done
```

In the app on both hostnames: map renders from PMTiles (storage CORS), filters + KPIs load (API CORS), `/bridge/{state}/{id}` deep link and `/qa` work (SWA fallback rewrite). Dispatch **Deploy** with `run_e2e: true` for the full smoke if anything looks off.

Failure modes: `static-web-app` step times out → www CNAME hadn't propagated (§8.1) or the apex TXT went in too late (§8.2 — re-run); cert "provisioning" → wait, it can lag validation by ~15 min; CORS errors on a new origin only → confirm the container app revision picked up `Cors__Origins__1/2` (env list in the portal's read-only revision view — env changes create a new revision automatically); apex serves but www doesn't (or vice versa) → check the corresponding record with `dig`, the two hostnames are independent bindings.

## 9. Enabling the AI assist — the Phase 0.5 gate (added 2026-07-24)

FR-AI.1 (Ask the Map) is built dark: code, tests, and the SPA affordance all shipped in Phase 0; `/api/ai/query` answers 503 ProblemDetails until a provider is configured. This section is the flip. It runs in parallel with Phase 1 W1 and does not gate Phase 1 (IMPLEMENTATION-PLAN §10 W0).

**The contract** (implemented by the `infra/ai-flip` wiring PR — a [CC] Claude Code task; this section is authoritative for names):

| Piece | Name | Set by |
|---|---|---|
| GitHub Actions **secret** | `ANTHROPIC_API_KEY` | **[RAZIEL]** only |
| GitHub Actions **variable** (the flip) | `AI_ENABLED` = `true` / `false` | **[RAZIEL]** |
| Bicep params (from workflow env, like `API_IMAGE`) | `@secure()` `anthropicApiKey` · `aiEnabled` | `deploy.yml` passthrough |
| ACA secret (declared in Bicep only when the key param is non-empty) | `anthropic-api-key` | Bicep |
| Container env | `ANTHROPIC_API_KEY` (secretRef) · `Ai__Enabled` · `Ai__Provider=anthropic` · `Ai__Model=claude-haiku-4-5` (ADR-008 pin) | Bicep |

Fail-closed by construction: flag off **or** key absent → the provider never registers and the endpoint stays on its dark 503 path (`Program.cs`). The key never exists in the repo, a prompt, or deployment history (`@secure()` params are not logged). Cost governors are already in the app: 200 requests/day budget trip, 512-token output cap, 24 h normalized-input cache, 10 req/min/IP rate limit — worst-case day on a Haiku-class model is pennies (target ≤$5/mo inside NFR-2, ADR-008 §4).

### 9.1 Pre-requisite

The `infra/ai-flip` PR is merged (it ships with the flag off — merging changes nothing live). It also adds `UserSecretsId` to `SpanSight.Api.csproj` for §9.3.

### 9.2 Create the key **[RAZIEL]**

Anthropic Console (personal account) → API keys → create a key named `spansight-demo`. Set a **monthly spend limit** (~$5) in the console's billing limits. Key goes in the password manager; it will only ever be pasted into `dotnet user-secrets` (§9.3) and the GitHub secret (§9.4).

### 9.3 Local smoke first (recommended) **[RAZIEL]**

```bash
dotnet user-secrets set "Ai:Enabled" "true"                --project src/SpanSight.Api
dotnet user-secrets set "Ai:Model" "claude-haiku-4-5"      --project src/SpanSight.Api
dotnet user-secrets set "Ai:ApiKey" "<key>"                --project src/SpanSight.Api
dotnet run --project src/SpanSight.Api   # then, in another shell:
curl -s -X POST http://localhost:5194/api/ai/query -H 'Content-Type: application/json' \
  -d '{"text":"poor truss bridges in florida built before 1970"}'
```

Expect 200 with `state: FL`, `conditions: ["Poor"]`, `typeGroups: ["Truss / Arch"]`, `yearBuiltMax: 1969` — the same rail values the browser smoke pinned against the stub. (`Ai:ApiKey` in user-secrets lives outside the repo tree by design.)

### 9.4 Flip the cloud **[RAZIEL]**

Repo → Settings → Secrets and variables → Actions: **secret** `ANTHROPIC_API_KEY` = the key · **variable** `AI_ENABLED` = `true`. Then Actions → **Deploy** → Run workflow (`run_e2e: true` recommended).

### 9.5 Live-key smoke — the FR-AI.1 AC-6 gate item **[RAZIEL]**

```bash
API=https://ca-spansight-api-demo.wonderfulforest-bd8cc0ce.southcentralus.azurecontainerapps.io
# 1. Translation (fresh):
curl -s -X POST "$API/api/ai/query" -H 'Content-Type: application/json' \
  -d '{"text":"poor truss bridges in florida built before 1970"}'
# 2. Same body again → served from cache (visibly faster, same payload):
# 3. Guardrail: a judgment request lands in `unsupported`, filters unaffected:
curl -s -X POST "$API/api/ai/query" -H 'Content-Type: application/json' \
  -d '{"text":"which bridges are unsafe to drive on?"}'
```

Then the real thing: **https://www.spansights.com** → Ask the Map → same phrase → rail, KPIs, map, and results move together (AC-2), interpretation line shows the applied values (AC-3).

### 9.6 Close-out

RTM FR-AI.1 → **Done** with this smoke as evidence + CLAUDE.md status line ([CC] task) · check the Anthropic console usage page after a few days (spend ≈ pennies; the 200/day budget and cache are doing their jobs) · Azure spend unchanged (no new resources).

**Rollback:** set `AI_ENABLED` = `false` → run **Deploy** → endpoint back to its dark 503 path, SPA shows the built-in notice. Key compromise: revoke in the Anthropic console first, then rotate the GitHub secret.

## 10. Phase 1 data ops — the vintage Parquet archive (added 2026-07-26)

The 34-vintage NBI history (1992–2025) is built on the dev Mac and archived to Blob cool tier. It
never touches the serving database: the historical set lives in Parquet and is read by DuckDB
(ADR-005), so **nothing in this section affects the live demo**. Both commands are safe to re-run.

### 10.1 Build the Parquet set **[RAZIEL or CC]**

```bash
tools/vintages/download.sh    # ~1.6 GB of zips from FHWA; skips what it already has
tools/vintages/convert.sh     # → data/vintages/parquet/ (~1.4 GB) + tools/vintages/catalog.json
```

`convert.sh` fails loudly if any vintage does not reconcile, so a green run is the check. Confirm
with a query rather than by eye:

```bash
duckdb -init tools/vintages/catalog.sql -c "SELECT bool_and(reconciles) FROM nbi_reconciliation"
```

The 2026-07-26 run: 22,307,363 source rows = 22,307,362 converted + 1 rejected, all 34 reconciling.

### 10.2 Archive it **[RAZIEL]** — the az login is yours

```bash
tools/vintages/archive-to-blob.sh --dry-run    # lists 34 files + catalog.json, contacts nothing
tools/vintages/archive-to-blob.sh              # uploads to stspansightdemo/parquet-archive (Cool)
```

The script never runs `az login`; it fails with instructions if you are not already logged in
(CLAUDE.md rule 2 — account actions stay with you). The `parquet-archive` container and its
cool-tier lifecycle rule are already declared in `infra/modules/storage.bicep`, so this needs no
deploy and adds no resource — only ~1.4 GB of cool-tier blobs, well under a dollar a month against
the $50 budget (NFR-2).

Verify:

```bash
az storage blob list --account-name stspansightdemo --container-name parquet-archive \
  --auth-mode key --query "length(@)"     # expect 35: 34 vintages + catalog.json
```

**Rollback / cost-out:** the set is fully reproducible from FHWA with §10.1, so deleting the
container costs nothing but the rebuild time. `az storage blob delete-batch --account-name
stspansightdemo --source parquet-archive --auth-mode key`.
