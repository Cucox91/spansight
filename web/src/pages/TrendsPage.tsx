import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { fetchLookups, fetchTrends } from '../api/client'
import type { Lookups, TrendPoint, TrendSeries } from '../api/types'

const FIRST_VINTAGE = 1992
const LAST_VINTAGE = 2025

type Level = 'state' | 'county'

/**
 * FR-1.2 AC-2 — Good/Fair/Poor shares over time for a state or county.
 *
 * The whole query lives in the URL (level, fips, fromYear, toYear), so a view is shareable and
 * reload-safe the same way the bridge drawer is (UC-0.3). Everything shown is a count of published
 * NBI ratings — the method note travels with the chart (GR-6).
 */
export default function TrendsPage() {
  const [params, setParams] = useSearchParams()
  const level = (params.get('level') === 'county' ? 'county' : 'state') as Level
  const fips = params.get('fips') ?? '01'
  const fromYear = clampYear(params.get('fromYear'), FIRST_VINTAGE)
  const toYear = clampYear(params.get('toYear'), LAST_VINTAGE)

  const [series, setSeries] = useState<TrendSeries | null>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [message, setMessage] = useState('')
  const [lookups, setLookups] = useState<Lookups | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchLookups(controller.signal)
      .then(setLookups)
      .catch(() => {
        /* The state picker degrades to the FIPS box; the chart does not depend on it. */
      })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    setStatus('loading')
    fetchTrends(level, fips, fromYear, toYear, controller.signal)
      .then((data) => {
        setSeries(data)
        setStatus('ready')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        setMessage(error instanceof Error ? error.message : 'Request failed')
        setStatus('error')
      })
    return () => controller.abort()
  }, [level, fips, fromYear, toYear])

  const update = (next: Record<string, string>) => {
    const merged = new URLSearchParams(params)
    for (const [key, value] of Object.entries(next)) {
      merged.set(key, value)
    }
    setParams(merged, { replace: false })
  }

  return (
    <div className="trends-page">
      <h2>Condition over time</h2>
      <p className="qa-meta">
        Share of structures in each FHWA condition class, by NBI vintage. Counts come from the
        published inspection ratings in each year&apos;s national file — nothing is modelled.
      </p>

      <form className="trends-controls" onSubmit={(event) => event.preventDefault()}>
        <label>
          <span>Level</span>
          <select
            value={level}
            onChange={(event) =>
              update({
                level: event.target.value,
                // A 5-digit county FIPS is not a valid state key and vice versa, so switching
                // level resets the area rather than firing a request that must 400.
                fips: event.target.value === 'county' ? '01001' : '01',
              })
            }
          >
            <option value="state">State</option>
            <option value="county">County</option>
          </select>
        </label>

        {level === 'state' && lookups ? (
          <label>
            <span>State</span>
            <select value={fips} onChange={(event) => update({ fips: event.target.value })}>
              {lookups.states.map((s) => (
                <option key={s.fips} value={s.fips}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <label>
            <span>{level === 'county' ? 'County FIPS' : 'State FIPS'}</span>
            <input
              type="text"
              inputMode="numeric"
              defaultValue={fips}
              aria-describedby="fips-help"
              onBlur={(event) => update({ fips: event.target.value.trim() })}
            />
          </label>
        )}

        <label>
          <span>From</span>
          <input
            type="number"
            min={FIRST_VINTAGE}
            max={LAST_VINTAGE}
            defaultValue={fromYear}
            onBlur={(event) => update({ fromYear: event.target.value })}
          />
        </label>

        <label>
          <span>To</span>
          <input
            type="number"
            min={FIRST_VINTAGE}
            max={LAST_VINTAGE}
            defaultValue={toYear}
            onBlur={(event) => update({ toYear: event.target.value })}
          />
        </label>
      </form>

      {level === 'county' && (
        <p className="qa-meta" id="fips-help">
          County FIPS is the 2-digit state code followed by the 3-digit county code — e.g.{' '}
          <code>12086</code> for Miami-Dade, Florida. County names arrive with the TIGER join
          (FR-1.5).
        </p>
      )}

      {status === 'loading' && <div className="drawer-state">Loading condition trend…</div>}
      {status === 'error' && (
        <div className="drawer-state" role="alert">
          Could not load this trend — {message}
        </div>
      )}

      {status === 'ready' && series && <TrendChart series={series} />}
    </div>
  )
}

function clampYear(raw: string | null, fallback: number): number {
  const parsed = Number(raw)
  if (!Number.isInteger(parsed) || parsed < FIRST_VINTAGE || parsed > LAST_VINTAGE) {
    return fallback
  }
  return parsed
}

function TrendChart({ series }: { series: TrendSeries }) {
  const { points } = series
  const maxTotal = useMemo(() => Math.max(1, ...points.map((p) => p.total)), [points])

  if (points.length === 0) {
    return (
      <div className="drawer-state">
        No published rows for {series.name} between {series.fromYear} and {series.toYear}. If this
        is a county, check the FIPS code — and note that Connecticut replaced its counties with
        planning regions in 2022, so its older county codes stop appearing.
      </div>
    )
  }

  return (
    // Focusable so keyboard users can scroll the stack of year rows (axe
    // scrollable-region-focusable, NFR-7) — the same treatment the QA report gets.
    <div className="trend-result" role="region" aria-label={`Condition over time for ${series.name}`} tabIndex={0}>
      <h3>{series.name}</h3>
      <p className="qa-meta">
        {points.length} vintage{points.length === 1 ? '' : 's'} · {points[0].year}–
        {points[points.length - 1].year} ·{' '}
        {points[points.length - 1].total.toLocaleString()} structures in the latest year shown
      </p>

      <div className="trend-bars">
        {points.map((point) => (
          <YearRow key={point.year} point={point} maxTotal={maxTotal} />
        ))}
      </div>

      {/* Same dot-plus-label shape as the map legend: .chip is a swatch, the text sits beside it
          so it keeps the page's normal contrast rather than sitting on the swatch colour. */}
      <ul className="trend-legend">
        <li>
          <span className="chip good" /> Good (lowest rating ≥ 7)
        </li>
        <li>
          <span className="chip fair" /> Fair (5–6)
        </li>
        <li>
          <span className="chip poor" /> Poor (≤ 4)
        </li>
        <li>
          <span className="chip unknown" /> Unrated (no numeric rating published)
        </li>
      </ul>

      {/* GR-6: the method sits with the chart, not in a footnote elsewhere. */}
      <p className="history-method">{series.method}</p>
      {series.provenance && (
        <p className="qa-meta">
          Computed offline from the NBI vintage catalog · job {series.provenance.jobRunId} · catalog{' '}
          <code>{series.provenance.catalogSha256.slice(0, 12)}</code>
          {series.provenance.publishedUtc
            ? ` · published ${new Date(series.provenance.publishedUtc).toLocaleDateString()}`
            : ''}
        </p>
      )}
    </div>
  )
}

function YearRow({ point, maxTotal }: { point: TrendPoint; maxTotal: number }) {
  // Bar width tracks the population of the year, so a year with far fewer structures does not read
  // as an equally-confident row; the segments inside it are the condition shares.
  const scale = (100 * point.total) / maxTotal
  const segment = (count: number) => (point.total === 0 ? 0 : (100 * count) / point.total)

  return (
    <div className="trend-row">
      <span className="trend-year">{point.year}</span>
      <div className="trend-track">
        <div className="trend-stack" style={{ width: `${scale}%` }}>
          <div className="seg good" style={{ width: `${segment(point.good)}%` }} />
          <div className="seg fair" style={{ width: `${segment(point.fair)}%` }} />
          <div className="seg poor" style={{ width: `${segment(point.poor)}%` }} />
          <div className="seg unknown" style={{ width: `${segment(point.unknown)}%` }} />
        </div>
      </div>
      <span className="trend-figure">
        {point.poorShare != null ? `${point.poorShare.toFixed(1)}% Poor` : '—'}
        <small>
          {point.total.toLocaleString()} structures · {point.good.toLocaleString()} Good ·{' '}
          {point.fair.toLocaleString()} Fair · {point.poor.toLocaleString()} Poor
          {point.unknown > 0 ? ` · ${point.unknown.toLocaleString()} unrated` : ''}
        </small>
      </span>
    </div>
  )
}
