// One filter predicate, one source of truth (DESIGN.md): the rail edits this state, and the
// map layer, KPI strip, and result queries all derive their API querystring from it.

export const TYPE_GROUPS = {
  'Girder / Stringer': ['01', '02', '03', '04', '05', '06', '21', '22'],
  'Truss / Arch': ['09', '10', '11', '12', '13', '14'],
  Culvert: ['19'],
  Other: ['00', '07', '08', '15', '16', '17', '18', '20'],
} as const

export type TypeGroup = keyof typeof TYPE_GROUPS

export type ConditionOption = 'Good' | 'Fair' | 'Poor'

export interface FilterState {
  conditions: ConditionOption[]
  state: string // USPS abbreviation or '' for all
  typeGroups: TypeGroup[]
  yearBuiltMax: number | null
  minAdt: number
}

export const ALL_CONDITIONS: ConditionOption[] = ['Good', 'Fair', 'Poor']
export const ALL_TYPE_GROUPS = Object.keys(TYPE_GROUPS) as TypeGroup[]

export const DEFAULT_FILTERS: FilterState = {
  conditions: ALL_CONDITIONS,
  state: '',
  typeGroups: ALL_TYPE_GROUPS,
  yearBuiltMax: null,
  minAdt: 0,
}

/** Serializes the predicate for the API. All-selected dimensions are omitted entirely. */
export function toSearchParams(filters: FilterState): URLSearchParams {
  const params = new URLSearchParams()

  if (filters.conditions.length > 0 && filters.conditions.length < ALL_CONDITIONS.length) {
    for (const condition of filters.conditions) params.append('condition', condition)
  }

  if (filters.state) params.set('state', filters.state)

  if (filters.typeGroups.length > 0 && filters.typeGroups.length < ALL_TYPE_GROUPS.length) {
    for (const group of filters.typeGroups) {
      for (const design of TYPE_GROUPS[group]) params.append('design', design)
    }
  }

  if (filters.yearBuiltMax !== null) params.set('yearBuiltMax', String(filters.yearBuiltMax))
  if (filters.minAdt > 0) params.set('minAdt', String(filters.minAdt))

  return params
}

export function filtersKey(filters: FilterState): string {
  return toSearchParams(filters).toString()
}
