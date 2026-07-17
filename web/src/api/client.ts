import type {
  BridgeDetail,
  BridgeFeatureCollection,
  BridgeSummary,
  Lookups,
  PagedResponse,
  QaSummary,
  StatsSummary,
} from './types'
import { toSearchParams, type FilterState } from '../state/filters'

// Dev: Vite proxies /api to the local API (vite.config.ts). Prod: Static Web Apps routes /api
// to the Container App (ADR-006-B), so same-origin paths work in both.
const BASE = ''

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

export function fetchQaSummary(signal?: AbortSignal): Promise<QaSummary> {
  return getJson('/api/qa/summary', signal)
}

export function fetchLookups(signal?: AbortSignal): Promise<Lookups> {
  return getJson('/api/lookups', signal)
}
