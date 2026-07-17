import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { DEFAULT_FILTERS, type FilterState } from './filters'

interface FiltersContextValue {
  filters: FilterState
  setFilters: (update: (previous: FilterState) => FilterState) => void
  reset: () => void
}

const FiltersContext = createContext<FiltersContextValue | null>(null)

export function FiltersProvider({ children }: { children: ReactNode }) {
  const [filters, setFiltersState] = useState<FilterState>(DEFAULT_FILTERS)

  const value = useMemo<FiltersContextValue>(
    () => ({
      filters,
      setFilters: (update) => setFiltersState((previous) => update(previous)),
      reset: () => setFiltersState(DEFAULT_FILTERS),
    }),
    [filters],
  )

  return <FiltersContext.Provider value={value}>{children}</FiltersContext.Provider>
}

export function useFilters(): FiltersContextValue {
  const context = useContext(FiltersContext)
  if (!context) {
    throw new Error('useFilters must be used inside FiltersProvider')
  }
  return context
}
