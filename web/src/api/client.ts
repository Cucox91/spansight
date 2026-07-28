import type {
  BridgeDetail,
  BridgeFeatureCollection,
  BridgeHistory,
  BridgeSummary,
  ConditionComponent,
  CountyReportCard,
  DeteriorationCatalog,
  DeteriorationMatrix,
  Lookups,
  NlQueryResponse,
  PagedResponse,
  QaSummary,
  Ranking,
  StatsSummary,
  TrendSeries,
} from './types'
import { toSearchParams, type FilterState } from '../state/filters'

// Dev: Vite proxies /api to the local API (vite.config.ts), so BASE stays ''. Prod: the SPA
// (Static Web Apps, Free tier — no linked backend) calls the Container App cross-origin;
// deploy.yml bakes VITE_API_BASE_URL in at build time and the API's CORS allowlists the SWA
// hostname (ADR-006-B, infra/modules/container-app.bicep).
const BASE: string = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${BASE}${path}`, { signal })
  if (!response.ok) {
    throw new Error(`API ${response.status} on ${path}`)
  }
  return (await response.json()) as T
}

export function fetchBridges(
  filters: FilterState,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
): Promise<PagedResponse<BridgeSummary>> {
  const params = toSearchParams(filters)
  params.set('page', String(page))
  params.set('pageSize', String(pageSize))
  return getJson(`/api/bridges?${params}`, signal)
}

export function fetchGeoJson(filters: FilterState, signal?: AbortSignal): Promise<BridgeFeatureCollection> {
  const params = toSearchParams(filters)
  return getJson(`/api/bridges/geojson?${params}`, signal)
}

export function fetchStats(filters: FilterState, signal?: AbortSignal): Promise<StatsSummary> {
  const params = toSearchParams(filters)
  return getJson(`/api/stats/summary?${params}`, signal)
}

export function fetchDetail(state: string, structureNumber: string, signal?: AbortSignal): Promise<BridgeDetail> {
  return getJson(`/api/bridges/${encodeURIComponent(state)}/${encodeURIComponent(structureNumber)}`, signal)
}

/**
 * FR-1.2 — a structure's published condition history. 404 is an ordinary outcome (the structure
 * predates or postdates the loaded vintages), so the caller distinguishes it from a real failure.
 */
export async function fetchHistory(
  state: string,
  structureNumber: string,
  signal?: AbortSignal,
): Promise<BridgeHistory | null> {
  const path = `/api/bridges/${encodeURIComponent(state)}/${encodeURIComponent(structureNumber)}/history`
  const response = await fetch(`${BASE}${path}`, { signal })
  if (response.status === 404) {
    return null
  }
  if (!response.ok) {
    throw new Error(`API ${response.status} on ${path}`)
  }
  return (await response.json()) as BridgeHistory
}

/** FR-1.2 — Good/Fair/Poor shares over time for one state or county. */
export function fetchTrends(
  level: 'state' | 'county',
  fips: string,
  fromYear: number,
  toYear: number,
  signal?: AbortSignal,
): Promise<TrendSeries> {
  const params = new URLSearchParams({
    level,
    fips,
    fromYear: String(fromYear),
    toYear: String(toYear),
  })
  return getJson(`/api/trends?${params}`, signal)
}

/** FR-1.3 — the cohorts that have published matrices, plus each dimension's vocabulary. */
export function fetchDeteriorationCatalog(signal?: AbortSignal): Promise<DeteriorationCatalog> {
  return getJson('/api/deterioration/cohorts', signal)
}

/**
 * FR-1.3 — one cohort's transition matrix for one component. Omitting the cohort asks for the
 * national all-cohorts context matrix, which is the exact sum of every cohort.
 */
export function fetchDeteriorationMatrix(
  component: ConditionComponent,
  cohort: { typeGroup: string; materialGroup: string; region: string } | null,
  signal?: AbortSignal,
): Promise<DeteriorationMatrix> {
  const params = new URLSearchParams({ component })
  if (cohort) {
    params.set('typeGroup', cohort.typeGroup)
    params.set('materialGroup', cohort.materialGroup)
    params.set('region', cohort.region)
  }
  return getJson(`/api/deterioration/matrix?${params}`, signal)
}

export function fetchQaSummary(signal?: AbortSignal): Promise<QaSummary> {
  return getJson('/api/qa/summary', signal)
}

export function fetchLookups(signal?: AbortSignal): Promise<Lookups> {
  return getJson('/api/lookups', signal)
}

/**
 * FR-AI.1 ask-the-map. Non-2xx responses surface the ProblemDetails detail (e.g. the Phase 0.5
 * "not enabled" message or the budget-exhausted notice) as the thrown error message.
 */
export async function askTheMap(text: string): Promise<NlQueryResponse> {
  const response = await fetch(`${BASE}/api/ai/query`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ text }),
  })
  if (!response.ok) {
    let detail = `API ${response.status}`
    try {
      const problem = (await response.json()) as { detail?: string; title?: string }
      detail = problem.detail ?? problem.title ?? detail
    } catch {
      // keep the status fallback
    }
    throw new Error(detail)
  }
  return (await response.json()) as NlQueryResponse
}

/** FR-1.4 AC-1 — a ranking with its definition. Params are passed straight through. */
export function fetchRanking(query: URLSearchParams, signal?: AbortSignal): Promise<Ranking> {
  return getJson(`/api/rankings?${query}`, signal)
}

/**
 * FR-1.4 AC-2 — one county's report card. Returns null on 404 so a mistyped FIPS renders an empty
 * state rather than the page's error branch (the same shape the bridge detail fetch uses).
 */
export async function fetchCountyReportCard(
  fips: string,
  signal?: AbortSignal,
): Promise<CountyReportCard | null> {
  const response = await fetch(`${BASE}/api/counties/${encodeURIComponent(fips)}`, { signal })
  if (response.status === 404) {
    return null
  }

  if (!response.ok) {
    throw new Error(`GET /api/counties/${fips} failed: ${response.status}`)
  }

  return (await response.json()) as CountyReportCard
}
