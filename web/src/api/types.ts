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
  /** 2-digit state FIPS — prefix of the county FIPS the trends view takes. */
  stateFips: string
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

/** FR-1.2 — which offline job produced the figures on screen. */
export interface TrendProvenance {
  jobRunId: string
  catalogSha256: string
  publishedUtc: string | null
}

export interface ConditionPoint {
  year: number
  lowestRating: number | null
  conditionClass: ConditionClass
}

/**
 * A structure's published condition history. `points` holds only the years FHWA published this
 * structure — a gap in first..last means no published record, never an estimate (GR-6).
 */
export interface BridgeHistory {
  id: string
  state: string
  structureNumber: string
  firstYear: number
  lastYear: number
  observedYears: number
  points: ConditionPoint[]
  method: string
  provenance: TrendProvenance
}

export interface TrendPoint {
  year: number
  total: number
  good: number
  fair: number
  poor: number
  unknown: number
  goodShare: number | null
  fairShare: number | null
  poorShare: number | null
  unknownShare: number | null
}

export interface TrendSeries {
  level: 'State' | 'County'
  fips: string
  name: string
  fromYear: number
  toYear: number
  points: TrendPoint[]
  method: string
  provenance: TrendProvenance | null
}

/* ---------------------------------------------------------------- FR-1.3 deterioration patterns */

export type ConditionComponent = 'Deck' | 'Superstructure' | 'Substructure' | 'Culvert'

/** Which offline job produced the matrices, and under which methodology version. */
export interface DeteriorationProvenance {
  jobRunId: string
  catalogSha256: string
  publishedUtc: string | null
  firstYear: number
  lastYear: number
  yearPairs: number
  componentPairs: number
}

/** A published NBI code with its decoded label — groups ship their members so labels stay honest. */
export interface CohortCode {
  code: string
  label: string
}

export interface CohortGroup {
  name: string
  codes: CohortCode[]
}

export interface DeteriorationDimensions {
  components: ConditionComponent[]
  typeGroups: CohortGroup[]
  materialGroups: CohortGroup[]
  regions: string[]
}

export interface CohortComponentSummary {
  component: ConditionComponent
  pairs: number
  rows: number
  rowsAboveFloor: number
}

export interface DeteriorationCohort {
  typeGroup: string
  materialGroup: string
  region: string
  /** The all-cohorts context matrix: the exact sum of every cohort. */
  isNational: boolean
  components: CohortComponentSummary[]
}

export interface DeteriorationCatalog {
  dimensions: DeteriorationDimensions
  cohorts: DeteriorationCohort[]
  sampleFloor: number
  method: string
  methodologyVersion: string
  methodologyUrl: string
  provenance: DeteriorationProvenance | null
}

/** `rate` is null whenever the containing row is below the sample-size floor — never render a number then. */
export interface DeteriorationCell {
  toRating: number
  pairs: number
  rate: number | null
}

export interface DeteriorationRow {
  fromRating: number
  rowTotal: number
  /** False → the view must say "insufficient data" rather than show a rate (FR-1.3 AC-3). */
  sufficient: boolean
  firstFromYear: number | null
  lastFromYear: number | null
  yearPairsObserved: number
}

export interface DeteriorationMatrixRow extends DeteriorationRow {
  cells: DeteriorationCell[]
}

export interface DeteriorationMatrix {
  component: ConditionComponent
  cohort: DeteriorationCohort
  rows: DeteriorationMatrixRow[]
  pairs: number
  sampleFloor: number
  rowsBelowFloor: number
  unchangedShare: number | null
  method: string
  cadenceCaption: string
  methodologyVersion: string
  methodologyUrl: string
  provenance: DeteriorationProvenance | null
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
  /** FR-1.5 AC-2. Null until the county join has been published — not zero coverage. */
  countyJoin: CountyJoinCoverage | null
}

// ============ CENSUS COUNTY JOIN (FR-1.5) ============

export interface CountyJoinMissReason {
  reason: string
  /** Every served row with this reason, all record types. */
  rows: number
  /** Of those, the record-type-1 structures. The two differ by nearly three to one. */
  structures: number
  /** Null when every miss in this group fell outside the job's bounded search radius. */
  medianDistanceMeters: number | null
  maxDistanceMeters: number | null
}

export interface CountyJoinDisagreement {
  nbiCountyFips: string | null
  /** Null when the boundary file no longer carries the published code — the Connecticut case. */
  nbiCountyName: string | null
  countyFips: string
  countyName: string
  kind: string
  /** Served rows taking this path, all record types. */
  rows: number
  /** Of those, the record-type-1 structures. */
  structures: number
  /** Null when no county code was published at all, since the question does not apply. */
  nbiFipsInTiger: boolean | null
}

/**
 * FR-1.5 AC-2 join coverage. Two denominators travel together on purpose: `structures` is NBI
 * record type 1 — what "bridge" means in the trend and pattern views — while `bridges` is every
 * row the serving table holds, including the routes published *under* a structure.
 */
export interface CountyJoinCoverage {
  bridges: number
  matched: number
  unmatched: number
  coveragePercent: number
  structures: number
  structuresMatched: number
  structureCoveragePercent: number
  agree: number
  agreePercent: number | null
  differentCountySameState: number
  differentState: number
  countyNotPublished: number
  counties: number
  countiesWithoutPopulation: number
  missesByReason: CountyJoinMissReason[]
  largestDisagreements: CountyJoinDisagreement[]
  rowsUnderRetiredCodes: number
  structuresUnderRetiredCodes: number
  methodNote: string
  provenance: {
    jobRunId: string
    catalogSha256: string
    methodVersion: string
    containmentPredicate: string
    completedUtc: string | null
  }
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

/* ---------------------------------------------------------------- FR-1.4 rankings + report cards */

/**
 * What a ranking is and what it is not — server-authored and rendered beside the rows, because
 * AC-1 requires the sort and inclusion rules to be displayed alongside the results.
 *
 * `excludedGroups` / `excludedStructures` are the pair that keeps the list honest: a ranking is an
 * ordering, so a group whose share cannot be published has no position in it and is left out —
 * these say how much was set aside.
 */
export interface RankingDefinition {
  headline: string
  sortedBy: string
  includes: string
  excludes: string
  /** Null for a structure-level list, which has no share and therefore no denominator. */
  denominator: string | null
  minimumGroupSize: number | null
  excludedGroups: number
  excludedStructures: number
  note: string
}

export interface RankingGroup {
  rank: number
  key: string
  label: string
  /** Present for state and county groupings — the deep link into the report card. */
  fips: string | null
  structures: number
  rated: number
  good: number
  fair: number
  poor: number
  unrated: number
  poorPercent: number
}

export interface RankingStructure {
  rank: number
  state: string
  stateFips: string
  structureNumber: string
  countyFips: string | null
  countyName: string | null
  adt: number
  conditionClass: ConditionClass
  lowestRating: number | null
  yearBuilt: number | null
  facilityCarried: string | null
  featuresIntersected: string | null
}

/** Exactly one of `groups` / `structures` is populated, decided by `view`. */
export interface Ranking {
  view: 'worst-condition' | 'high-adt-poor'
  groupBy: 'state' | 'county' | 'cohort' | null
  snapshotYear: number
  scope: string | null
  definition: RankingDefinition
  groups: RankingGroup[]
  structures: RankingStructure[]
  csvUrl: string
}

/** All fields null together where the Census publishes a boundary but no ACS row (FR-1.5 AC-3). */
export interface CountyPopulation {
  estimate: number | null
  marginOfError: number | null
  acsVintage: number | null
  acsPeriod: string | null
  acsTable: string | null
  citation: string
  note: string
}

export interface CountyReportCard {
  countyFips: string
  countyName: string
  stateFips: string
  state: string
  stateName: string
  snapshotYear: number
  structures: number
  rated: number
  good: number
  fair: number
  poor: number
  unrated: number
  /** Null, not zero, when nothing in the county carries a numeric rating. */
  poorPercent: number | null
  goodPercent: number | null
  fairPercent: number | null
  medianYearBuilt: number | null
  totalAdt: number | null
  population: CountyPopulation
  /** Null until the FR-1.2 trend job has published this county. */
  trend: TrendSeries | null
  countsNote: string
  methodNote: string
  csvUrl: string
}
