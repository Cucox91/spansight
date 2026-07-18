import { useEffect, useState } from 'react'
import { fetchStats } from '../api/client'
import type { StatsSummary } from '../api/types'
import { useFilters } from '../state/FiltersContext'
import { filtersKey } from '../state/filters'

export default function KpiStrip() {
  const { filters } = useFilters()
  const [stats, setStats] = useState<StatsSummary | null>(null)
  const key = filtersKey(filters)

  useEffect(() => {
    const controller = new AbortController()
    let retry: ReturnType<typeof setTimeout> | undefined
    const load = () =>
      fetchStats(filters, controller.signal)
        .then(setStats)
        .catch((error: unknown) => {
          // Keep retrying until the API answers so a race with its startup doesn't leave
          // the strip blank forever; the filter-change effect re-arms this anyway.
          if (!(error instanceof DOMException && error.name === 'AbortError')) {
            retry = setTimeout(load, 2500)
          }
        })
    load()
    return () => {
      controller.abort()
      clearTimeout(retry)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  const dash = '–'
  return (
    <div className="kpis" aria-live="polite">
      <div className="kpi">
        <div className="value">{stats ? stats.total.toLocaleString() : dash}</div>
        <div className="label">Bridges shown</div>
      </div>
      <div className="kpi">
        <div className="value poor">{stats?.percentPoor != null ? `${stats.percentPoor}%` : dash}</div>
        <div className="label">% Poor</div>
      </div>
      <div className="kpi">
        <div className="value">{stats?.medianAge != null ? `${stats.medianAge} yrs` : dash}</div>
        <div className="label">Median age</div>
      </div>
      <div className="kpi">
        <div className="value">
          {stats?.averageAdt != null ? `${Math.round(stats.averageAdt / 1000)}k` : dash}
        </div>
        <div className="label">Avg AADT</div>
      </div>
    </div>
  )
}
