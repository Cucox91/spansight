import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { fetchRanking } from '../api/client'
import type { Ranking } from '../api/types'

const VIEWS = [
  { value: 'worst-condition', label: 'Worst condition' },
  { value: 'high-adt-poor', label: 'High traffic, Poor condition' },
] as const

const GROUPINGS = [
  { value: 'county', label: 'By county' },
  { value: 'state', label: 'By state' },
  { value: 'cohort', label: 'By type, material and climate region' },
] as const

const LIMITS = [10, 25, 50, 100] as const

const BASE: string = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

/**
 * FR-1.4 AC-1/AC-3 — rankings, with the sort and inclusion rules displayed alongside the results.
 *
 * The whole query lives in the URL (view, groupBy, state, limit), so a ranking is shareable and
 * reload-safe the way the trends view and the bridge drawer are (UC-0.3).
 *
 * The definition block is not decoration. A ranking is an ordering, and an ordering is only
 * meaningful if a reader can see what was ordered, by what rule, and what the rule left out — so
 * the API authors all four sentences and the page renders them verbatim, inside the same region as
 * the rows (GR-6). Nothing here is a priority list.
 */
export default function RankingsPage() {
  const [params, setParams] = useSearchParams()
  const view = params.get('view') === 'high-adt-poor' ? 'high-adt-poor' : 'worst-condition'
  const groupBy = GROUPINGS.find((g) => g.value === params.get('groupBy'))?.value ?? 'county'
  const state = params.get('state') ?? ''
  const limit = LIMITS.find((l) => String(l) === params.get('limit')) ?? 25

  const [ranking, setRanking] = useState<Ranking | null>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [message, setMessage] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    setStatus('loading')

    const query = new URLSearchParams({ view, limit: String(limit) })
    // groupBy is rejected outright for the structure-level view rather than ignored, so it must
    // not be sent there — see RankingEndpoints.TryBuildQuery.
    if (view === 'worst-condition') {
      query.set('groupBy', groupBy)
    }
    if (state) {
      query.set('state', state)
    }

    fetchRanking(query, controller.signal)
      .then((result) => {
        setRanking(result)
        setStatus('ready')
      })
      .catch((error: unknown) => {
        // StrictMode double-invokes effects in dev, so the first request always aborts.
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setMessage(error instanceof Error ? error.message : 'Rankings are unavailable.')
        setStatus('error')
      })

    return () => controller.abort()
  }, [view, groupBy, state, limit])

  const update = (next: Record<string, string | null>) => {
    const merged = new URLSearchParams(params)
    for (const [key, value] of Object.entries(next)) {
      if (value === null || value === '') {
        merged.delete(key)
      } else {
        merged.set(key, value)
      }
    }
    setParams(merged, { replace: false })
  }

  return (
    <div className="rankings-page">
      <h2>Rankings</h2>
      <p className="qa-meta">
        Orderings of published NBI values by a stated rule. Every list carries the rule that produced
        it and the count of what that rule set aside — a ranking whose exclusions are invisible is a
        ranking a reader cannot check.
      </p>

      <form className="trends-controls" onSubmit={(e) => e.preventDefault()}>
        <label>
          View
          <select value={view} onChange={(e) => update({ view: e.target.value, groupBy: null })}>
            {VIEWS.map((v) => (
              <option key={v.value} value={v.value}>
                {v.label}
              </option>
            ))}
          </select>
        </label>

        {view === 'worst-condition' && (
          <label>
            Group by
            <select value={groupBy} onChange={(e) => update({ groupBy: e.target.value })}>
              {GROUPINGS.map((g) => (
                <option key={g.value} value={g.value}>
                  {g.label}
                </option>
              ))}
            </select>
          </label>
        )}

        <label>
          State (optional)
          <input
            type="text"
            value={state}
            placeholder="e.g. FL or 12"
            maxLength={2}
            onChange={(e) => update({ state: e.target.value.trim().toUpperCase() })}
          />
        </label>

        <label>
          Show
          <select value={limit} onChange={(e) => update({ limit: e.target.value })}>
            {LIMITS.map((l) => (
              <option key={l} value={l}>
                Top {l}
              </option>
            ))}
          </select>
        </label>
      </form>

      {status === 'loading' && <p className="qa-meta">Loading ranking…</p>}
      {status === 'error' && (
        <p className="qa-meta">
          {message} — is the API running? The ranking is computed from the loaded snapshot, so an
          empty database returns an empty list rather than an error.
        </p>
      )}

      {status === 'ready' && ranking && <RankingResult ranking={ranking} />}
    </div>
  )
}

function RankingResult({ ranking }: { ranking: Ranking }) {
  const { definition } = ranking
  const rows = ranking.groups.length + ranking.structures.length

  return (
    <div
      role="region"
      aria-label={definition.headline}
      tabIndex={0}
      className="ranking-result"
    >
      <h3>{definition.headline}</h3>

      {/* AC-1: the definition sits inside the same region as the rows, never in a tooltip or a
          collapsed panel — a reader can always see what the list is and what it is not. */}
      <dl className="ranking-definition">
        <dt>Sorted by</dt>
        <dd>{definition.sortedBy}</dd>
        <dt>Includes</dt>
        <dd>{definition.includes}</dd>
        <dt>Excludes</dt>
        <dd>{definition.excludes}</dd>
        {definition.denominator && (
          <>
            <dt>Share of</dt>
            <dd>{definition.denominator}</dd>
          </>
        )}
      </dl>

      {rows === 0 ? (
        <p className="qa-meta">
          No group in this snapshot meets the inclusion rule above. Nothing is hidden — the
          exclusions are counted in it.
        </p>
      ) : ranking.groups.length > 0 ? (
        <GroupTable ranking={ranking} />
      ) : (
        <StructureTable ranking={ranking} />
      )}

      <p className="qa-meta">{definition.note}</p>
      <p className="qa-meta">
        Snapshot {ranking.snapshotYear}
        {ranking.scope ? ` · ${ranking.scope}` : ' · national'} ·{' '}
        <a href={`${BASE}${ranking.csvUrl}`}>Download this ranking as CSV</a> — the export carries
        the same definition as leading comment lines.
      </p>
    </div>
  )
}

function GroupTable({ ranking }: { ranking: Ranking }) {
  return (
    <div className="qa-table-wrap">
      <table className="qa-table">
        <caption>
          {ranking.definition.headline}. Percentages are of rated structures; the unrated column is
          shown so a reader can see the denominator each share was taken from.
        </caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Group</th>
            <th scope="col">Structures</th>
            <th scope="col">Rated</th>
            <th scope="col">Good</th>
            <th scope="col">Fair</th>
            <th scope="col">Poor</th>
            <th scope="col">Unrated</th>
            <th scope="col">Poor %</th>
          </tr>
        </thead>
        <tbody>
          {ranking.groups.map((g) => (
            <tr key={g.key} data-rank={g.rank} data-key={g.key}>
              <td>{g.rank}</td>
              <th scope="row">
                {/* A county ranking links straight into its report card — the two views are the
                    same key, so the deep link is exact rather than a search. */}
                {ranking.groupBy === 'county' && g.fips ? (
                  <Link to={`/county/${g.fips}`}>{g.label}</Link>
                ) : (
                  g.label
                )}
              </th>
              <td>{g.structures.toLocaleString()}</td>
              <td>{g.rated.toLocaleString()}</td>
              <td>{g.good.toLocaleString()}</td>
              <td>{g.fair.toLocaleString()}</td>
              <td>{g.poor.toLocaleString()}</td>
              <td>{g.unrated.toLocaleString()}</td>
              <td>{g.poorPercent.toFixed(1)}%</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function StructureTable({ ranking }: { ranking: Ranking }) {
  return (
    <div className="qa-table-wrap">
      <table className="qa-table">
        <caption>
          {ranking.definition.headline}. Traffic is item 29 as published in this snapshot, and is not
          adjusted for the year it was counted.
        </caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Structure</th>
            <th scope="col">County</th>
            <th scope="col">Traffic (ADT)</th>
            <th scope="col">Lowest rating</th>
            <th scope="col">Built</th>
            <th scope="col">Carries</th>
            <th scope="col">Crosses</th>
          </tr>
        </thead>
        <tbody>
          {ranking.structures.map((s) => (
            <tr key={`${s.stateFips}-${s.structureNumber}`} data-rank={s.rank}>
              <td>{s.rank}</td>
              <th scope="row">
                <Link to={`/bridge/${s.state}/${encodeURIComponent(s.structureNumber)}`}>
                  {s.state}-{s.structureNumber}
                </Link>
              </th>
              <td>
                {s.countyFips ? (
                  <Link to={`/county/${s.countyFips}`}>{s.countyName ?? s.countyFips}</Link>
                ) : (
                  '—'
                )}
              </td>
              <td>{s.adt.toLocaleString()}</td>
              <td>{s.lowestRating ?? '—'}</td>
              <td>{s.yearBuilt ?? '—'}</td>
              <td className="ranking-text">{s.facilityCarried ?? '—'}</td>
              <td className="ranking-text">{s.featuresIntersected ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
