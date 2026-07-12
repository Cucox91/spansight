# SpanSight — Hosting Analysis: Multi-Vendor Free Tiers vs. Azure Consolidation

v1.2 · 2026-07-12 · *(v1.1 added §6: home-cluster option and $50/mo budget.)*

> **DECISION — 2026-07-12: ADR-006-B adopted in pure-Azure form.** No home cluster; all local development and heavy data work on the dev Mac; all runtime services on Azure within a **$50/mo budget** ($40 alert). The hybrid split in §6.2 was considered and declined. Now reflected in [ARCHITECTURE.md](./ARCHITECTURE.md) §5.2 + ADR-006-B and REQUIREMENTS.md NFR-2. This document remains as the decision record.

## 0. The questions this document answers

1. Are demo services hosted in different places? **Yes — four vendors.**
2. Was that to keep it free? **Yes — that was the only driver.**
3. Can we move (nearly) everything to Azure, and what does it cost? **Yes; ~$17–35/mo demo, table below.**
4. Why was multi-vendor chosen, and what are the long-term effects? **§3–§5.**

## 1. Current topology (ADR-006) and why each piece lives where it does

| Component | Current host | Why there |
|---|---|---|
| API + SignalR + poller | Azure Container Apps | Free grant (180K vCPU-s + 2M req/mo); Azure resume signal |
| PostgreSQL + PostGIS | Neon | Best free Postgres tier (~0.5 GB, scale-to-zero) |
| SPA + PMTiles | Cloudflare Pages / GitHub Pages | Free static hosting + CDN, no bandwidth anxiety |
| Redis | Upstash (candidate) | Free tier; Azure Redis has no free tier |
| Observability | Grafana Cloud | Forever-free 10K series / 50 GB logs+traces |
| Repo + CI | GitHub | Industry standard; free Actions minutes |

**The decision rule was one-dimensional: NFR-2 (~$0 target, $20 cap).** Each component sits on the most generous free tier for its job. Nothing else was optimized.

## 2. What multi-vendor actually costs you (the non-dollar price)

- **Cross-cloud latency — the big one.** Neon runs on AWS; the API runs on Azure. Every query crosses cloud boundaries: typically +10–40 ms per round trip depending on region pairing. Fine for a demo, wrong for a product.
- **Operational sprawl.** Four accounts, four consoles, four secret stores, four status pages; no unified billing, IAM, or alerting.
- **Networking ceiling.** No VNet/private endpoints across vendors — the DB is reachable only over public TLS. Acceptable demo posture, not product posture.
- **Free-tier fragility.** Any vendor can change terms (Fly.io already killed its free tier); four vendors = four such risks.
- **Narrative dilution.** "Deployed across four free tiers" reads as hobbyist; "runs on Azure with IaC and managed identity" reads as professional.

What multi-vendor does well: $0, and each component is independently swappable.

## 3. Azure consolidation mapping

| Component | Azure service | Demo monthly cost (East US, PAYG, July 2026) |
|---|---|---|
| API + SignalR + poller | Container Apps (consumption) — unchanged | $0 within [free grant](https://azure.microsoft.com/en-us/pricing/details/container-apps/) while scale-to-zero; **~$10–15** once the poller runs 24/7 (est. 0.25 vCPU/0.5 GiB always-on, net of grant) |
| PostgreSQL + PostGIS | [Database for PostgreSQL Flexible Server](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/), Burstable **B1ms** (1 vCPU/2 GiB) | **~$13** compute + **~$4** (32 GiB storage) ≈ **$17**. PostGIS is a supported extension — ADR-001 unchanged. New Azure accounts have historically gotten 750 B1ms hrs/mo free for 12 months — verify at signup |
| SPA hosting | [Static Web Apps](https://azure.microsoft.com/en-us/pricing/details/app-service/static/) Free (100 GB bw, custom domain) | **$0** (Standard **$9/mo** adds SLA, 500 GB bw — optional) |
| PMTiles | Blob Storage (hot LRS) + SWA/CDN in front | **< $0.50** for ~2 GB + demo egress |
| Redis | Redis container inside the Container Apps environment (demo-grade) | **~$0** (shares grant). Product-grade: [Azure Managed Redis](https://azure.microsoft.com/en-us/pricing/details/managed-redis/) from roughly **$11–16/mo** at the smallest tiers |
| Observability | Application Insights / [Azure Monitor](https://azure.microsoft.com/en-us/pricing/details/monitor/) (workspace-based, OTel exporters) | **$0** — first 5 GB/mo ingestion free per billing account; demo telemetry with sampling stays under it. Overage $2.30/GB (Analytics), $0.50/GB (Basic logs) |
| Repo + CI | Stays on GitHub | $0 (GitHub Actions deploys to Azure via OIDC federation — no stored credentials, another good talking point) |

### Cost scenarios

| Scenario | Topology | Monthly |
|---|---|---|
| A — Status quo | 4-vendor free tiers | **$0** (cap $20) |
| B — Azure demo, Phases 0–1 | Table above, scale-to-zero, batch-only | **≈ $17–20** |
| B+ — Azure demo, Phase 2+ | + 24/7 poller, + SWA Standard (optional) | **≈ $30–40** |
| C — Small real product | GP-tier Postgres w/ HA + backups, Managed Redis, always-on ACA, Front Door/WAF, Monitor at volume, support plan | **≈ $250–500+** (order-of-magnitude; re-quote at the time) |

Numbers are point-in-time (July 2026) list prices; set a **budget alert at $25/mo** (demo) regardless.

## 4. Long-term implications of consolidating on Azure

**What you gain**
- **Performance & posture:** API↔DB in-region (single-digit ms), VNet + private endpoints when wanted, WAF path via Front Door at product stage.
- **Identity done right:** Managed Identity end-to-end — API→Postgres, API→Blob, GitHub→Azure (OIDC). No connection-string secrets anywhere. This is a differentiating interview topic.
- **One pane:** single bill, single IAM, App Insights distributed tracing native to the .NET ecosystem you're selling yourself on.
- **Product runway:** if SpanSight ever serves government/agency customers, Azure's compliance surface (FedRAMP/StateRAMP-adjacent offerings, Azure Government) is the natural home — subject to GR-5 ethics clearance before any commercialization.
- **Resume coherence:** ".NET + React + Azure, provisioned with Bicep, deployed by GitHub Actions" is exactly the sentence your target roles want.

**What you give up / risks**
- **$17–40/mo real money** from day one (vs $0), rising with usage.
- **Mild lock-in** — mitigated by the stack itself: Postgres, Redis, containers, and OpenTelemetry are portable OSS. Leaving Azure later is config-and-data migration, not a rewrite. The only Azure-proprietary surface is App Insights dashboards/queries (KQL) and Bicep templates.
- **Azure SQL temptation resurfaces** — it shouldn't: ADR-001 (PostGIS) is unaffected; Azure *Database for PostgreSQL* is the consolidation target, not Azure SQL.
- **Free-grant math on ACA still applies** — consolidation doesn't change compute economics, only where everything else lives.

## 5. Recommendation (proposed ADR-006-B)

**Adopt Scenario B from Phase 0**, given explicit willingness to fund the demo:

1. All runtime services on Azure: ACA + PostgreSQL Flexible B1ms + Static Web Apps + Blob; Redis as ACA sidecar until Phase 2 scale demands otherwise.
2. Observability on Application Insights via OpenTelemetry exporters (vendor-neutral instrumentation, Azure-native backend). Grafana Cloud drops out.
3. GitHub stays for repo/CI with OIDC-federated deploys.
4. **Everything provisioned with Bicep from day one** — IaC becomes a first-class portfolio artifact instead of a stretch goal.
5. Cost governance: Azure budget alert at $25/mo, cost line item on the observability dashboard, monthly review.
6. NFR-2 amended when adopted: "~$0" → "≤ $25/mo demo budget, $40 hard cap" (edit to REQUIREMENTS.md deferred until you approve).

**If you later prefer $0 again:** Scenario A remains fully documented in ADR-006; reverting is a config exercise precisely because the components are portable.

## 6. Third option: the home cluster (on-prem) — added after discussion, budget raised to $50/mo

### 6.1 Azure vs. home cluster

| | Azure | Home cluster |
|---|---|---|
| **Short-term pros** | Demo uptime a hiring manager can trust; resume-aligned (".NET on Azure, Bicep, managed identity"); zero maintenance time; PaaS talking points | Effectively unlimited compute/storage already paid for: full 30-yr NBI history in PostGIS, DuckDB runs, tile builds, staging env, self-hosted Grafana, k6 load tests — no usage anxiety; adds platform/infra-ops skill signal |
| **Short-term cons** | Real money monthly; tiny compute per dollar (B1ms = 1 vCPU/2 GiB); grant/cost babysitting | No SLA where it matters most — residential ISP + power, dynamic IP, upload caps, ISP ToS usually prohibit hosting; security surface at home if exposed; electricity ≈ **$11–33/mo** at FL's ~[14–16¢/kWh](https://www.energysage.com/local-data/electricity-cost/fl/) for 100–300 W continuous (≈$0 marginal if already on 24/7); maintenance time competes with the 26-week plan; zero Azure resume signal by itself |
| **Long-term pros** | Product path: SLA, compliance, DDoS, scale, Azure Government for agency customers | Permanent value as dev/staging/data plane; free product prototyping before cloud commitment; deep ops skills |
| **Long-term cons** | Costs scale with usage; mild lock-in (portable-OSS stack keeps it small) | Cannot host a sellable product (no SLA/compliance/DDoS posture); hardware ages and fails on your time |

### 6.2 Recommended: hybrid split (ADR-006-B v2)

**Cluster = build/data plane. Azure = serving plane.**

| Plane | Runs | Why there |
|---|---|---|
| Home cluster | Full-history PostGIS + Parquet/DuckDB analytics · tippecanoe tile builds · staging environment · self-hosted Grafana sandbox · load testing | Big compute, zero cloud cost, no public exposure needed |
| Azure | Public demo only: ACA (API/SignalR/poller) · PostgreSQL Flexible B1ms (serving subset + aggregates) · Static Web Apps · Blob (PMTiles) · App Insights | Uptime, resume signal, managed identity/Bicep showcase |
| Flow | Cluster crunches → publishes artifacts (aggregate tables, PMTiles) → Azure serves | Standard build-plane/serving-plane pattern; doubles the interview story |

Optional middle path: a clearly-marked **"labs" instance** (e.g., full-history explorer) exposed from the cluster via free Cloudflare Tunnel — no open ports, hides home IP. Primary demo never depends on it. Self-hosted GitHub Actions runners only if the repo stays private or runners are isolated — public-repo self-hosted runners are a known attack vector.

### 6.3 Budget at $50/mo

| Item | Phases 0–1 | Phase 2+ |
|---|---|---|
| Azure serving plane | ~$17–20 | ~$30–35 (24/7 poller; optional +$9 SWA Standard SLA) |
| Domain (amortized) | ~$1 | ~$1 |
| Cluster electricity (marginal) | $0–15 (depends on current duty cycle) | same |
| **Total vs. $50 budget** | **~$20–35** | **~$35–50** |

Governance: Azure budget alert at **$40**, hard stop conversation at $50. NFR-2 amendment on adoption: "≤ $50/mo all-in during initial phases."

### 6.4 Open inputs that tune (not change) the recommendation

1. Cluster platform + specs (Kubernetes? Proxmox? nodes/RAM/storage) — sizes the data plane.
2. Already running 24/7? — sets true marginal electricity cost.
3. ISP upload speed + static IP availability — bounds what "labs" can host credibly.

---

*Sources (accessed 2026-07-12): [Azure PostgreSQL Flexible Server pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/) · [Azure Static Web Apps pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/static/) · [Azure Managed Redis pricing](https://azure.microsoft.com/en-us/pricing/details/managed-redis/) · [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/) · [Azure Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/) · [Bytebase Postgres hosting comparison](https://www.bytebase.com/blog/postgres-hosting-options-pricing-comparison/)*
