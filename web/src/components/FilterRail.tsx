import { useEffect, useState } from 'react'
import { fetchLookups, fetchStats } from '../api/client'
import type { Lookups } from '../api/types'
import { useFilters } from '../state/FiltersContext'
import {
  ALL_TYPE_GROUPS,
  filtersKey,
  type ConditionOption,
  type TypeGroup,
} from '../state/filters'

const CONDITION_OPTIONS: Array<{ value: ConditionOption; chip: string }> = [
  { value: 'Good', chip: 'good' },
  { value: 'Fair', chip: 'fair' },
  { value: 'Poor', chip: 'poor' },
]

const AADT_OPTIONS = [
  { value: 0, label: 'Any' },
  { value: 10_000, label: '≥ 10,000' },
  { value: 50_000, label: '≥ 50,000' },
  { value: 100_000, label: '≥ 100,000' },
]

export default function FilterRail() {
  const { filters, setFilters, reset } = useFilters()
  const [lookups, setLookups] = useState<Lookups | null>(null)
  const [conditionCounts, setConditionCounts] = useState<Record<string, number>>({})

  useEffect(() => {
    const controller = new AbortController()
    fetchLookups(controller.signal).then(setLookups).catch(() => undefined)
    return () => controller.abort()
  }, [])

  // Per-condition counts respond to every other filter dimension (not to the condition
  // checkboxes themselves), so the numbers explain what each box would add or remove.
  const countsFilter = { ...filters, conditions: [] as ConditionOption[] }
  const countsKey = filtersKey(countsFilter)
  useEffect(() => {
    const controller = new AbortController()
    fetchStats(countsFilter, controller.signal)
      .then((stats) => setConditionCounts(stats.byCondition))
      .catch(() => undefined)
    return () => controller.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [countsKey])

  const toggleCondition = (condition: ConditionOption) =>
    setFilters((previous) => ({
      ...previous,
      conditions: previous.conditions.includes(condition)
        ? previous.conditions.filter((c) => c !== condition)
        : [...previous.conditions, condition],
    }))

  const toggleTypeGroup = (group: TypeGroup) =>
    setFilters((previous) => ({
      ...previous,
      typeGroups: previous.typeGroups.includes(group)
        ? previous.typeGroups.filter((g) => g !== group)
        : [...previous.typeGroups, group],
    }))

  return (
    <aside className="filters" aria-label="Filters">
      <h2>Condition</h2>
      {CONDITION_OPTIONS.map(({ value, chip }) => (
        <label key={value}>
          <span className={`chip ${chip}`} /> {value}
          <input
            type="checkbox"
            checked={filters.conditions.includes(value)}
            onChange={() => toggleCondition(value)}
          />
          <span className="count">{conditionCounts[value]?.toLocaleString() ?? ''}</span>
        </label>
      ))}

      <h2>State</h2>
      <select
        value={filters.state}
        aria-label="State"
        onChange={(event) => setFilters((previous) => ({ ...previous, state: event.target.value }))}
      >
        <option value="">All states</option>
        {lookups?.states.map((state) => (
          <option key={state.fips} value={state.abbreviation}>
            {state.abbreviation} — {state.name}
          </option>
        ))}
      </select>

      <h2>Structure type</h2>
      {ALL_TYPE_GROUPS.map((group) => (
        <label key={group}>
          <input
            type="checkbox"
            checked={filters.typeGroups.includes(group)}
            onChange={() => toggleTypeGroup(group)}
          />{' '}
          {group}
        </label>
      ))}

      <h2>Built in or before</h2>
      <input
        type="number"
        min={1697}
        max={2026}
        step={5}
        placeholder="Any year"
        aria-label="Built in or before"
        value={filters.yearBuiltMax ?? ''}
        onChange={(event) =>
          setFilters((previous) => ({
            ...previous,
            yearBuiltMax: event.target.value === '' ? null : Number(event.target.value),
          }))
        }
      />

      <h2>Min. daily traffic (AADT)</h2>
      <select
        value={filters.minAdt}
        aria-label="Minimum daily traffic"
        onChange={(event) =>
          setFilters((previous) => ({ ...previous, minAdt: Number(event.target.value) }))
        }
      >
        {AADT_OPTIONS.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>

      <button className="reset" onClick={reset}>
        Reset filters
      </button>
    </aside>
  )
}
