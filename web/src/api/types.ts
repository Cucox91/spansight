// DTO shapes mirrored from SpanSight.Api (Dtos.cs). Hand-maintained for now; OpenAPI-generated
// types are a candidate improvement once the contract settles.

export type ConditionClass = 'Good' | 'Fair' | 'Poor' | 'Unknown'

export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface BridgeSummary {
  id: string
  state: string
  countyCode: string | null
  facilityCarried: string | null
  featuresIntersected: string | null
  latitude: number
  longitude: number
  yearBuilt: number | null
  adt: number | null
  materialCode: string | null
  designCode: string | null
  conditionClass: ConditionClass
  lowestRating: number | null
}

export interface ConditionRating {
  code: string | null
  text: string
}

export interface BridgeDetail {
  id: string
  state: string
  stateName: string
  structureNumber: string
  recordType: string
  countyCode: string | null
  facilityCarried: string | null
  featuresIntersected: string | null
  locationText: string | null
  latitude: number
  longitude: number
  yearBuilt: number | null
  ageYears: number | null
  adt: number | null
  materialCode: string | null
  material: string
  designCode: string | null
  design: string
  structureLengthMeters: number | null
  deck: ConditionRating
  superstructure: ConditionRating
  substructure: ConditionRating
  culvert: ConditionRating
  lowestRating: number | null
  conditionClass: ConditionClass
  sourceFormat: string
  snapshotYear: number
}

export interface StatsSummary {
  total: number
  byCondition: Record<string, number>
  percentPoor: number | null
  medianAge: number | null
  averageAdt: number | null
}

export interface GeoJsonMeta {
  total: number
  returned: number
  truncated: boolean
}

export interface BridgeFeatureCollection {
  type: 'FeatureCollection'
  features: Array<{
    type: 'Feature'
    geometry: { type: 'Point'; coordinates: [number, number] }
    properties: BridgeSummary
  }>
  meta: GeoJsonMeta
}

export interface IngestionRun {
  id: number
  sourceFile: string
  snapshotYear: number
  startedUtc: string
  completedUtc: string | null
  status: string
  rowsRead: number
  rowsLoaded: number
  rowsQuarantined: number
  rejectRate: number
}

export interface QaSummary {
  latestRun: IngestionRun | null
  byReason: Array<{ reason: string; count: number }>
  byState: Array<{ stateCode: string; state: string; count: number }>
}

export interface Lookups {
  states: Array<{ fips: string; abbreviation: string; name: string }>
  materials: Record<string, string>
  designs: Record<string, string>
  conditionRatings: Record<string, string>
  conditionClasses: string[]
}

/** FR-AI.1 response: the rail-shaped validated filter + code-rendered interpretation. */
export interface NlQueryResponse {
  filter: {
    state: string | null
    conditions: string[]
    typeGroups: string[]
    yearBuiltMax: number | null
    minAdt: number | null
  }
  interpretation: string
  unsupported: string[]
}
