import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { fetchCountyReportCard } from '../api/client'
import type { CountyReportCard } from '../api/types'

const BASE: string = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

/**
 * FR-1.4 AC-2 / FR-1.5 AC-3 — the county report card at `/county/:fips`.
 *
 * The FIPS is a path param, not a query param, because it identifies the thing rather than
 * filtering it — the same distinction `/bridge/:state/:structureNumber` makes.
 *
 * Everything here is keyed on the county code FHWA publishes in item 3. That is what makes the
 * counts, the trend series and the map's county filter describe the same set of structures; the
 * FR-1.5 point-in-polygon join measures how often the coordinate disagrees and publishes that on
 * the data-quality page, but it never overrides the publisher (GR-6).
 */
export default function CountyPage() {
  const { fips = '' } = useParams()
  const [card, setCard] = useState<CountyReportCard | null>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'missing' | 'error'>('loading')
  const [message, setMessage] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    setStatus('loading')

    fetchCountyReportCard(fips, controller.signal)
      .then((result) => {
        setCard(result)
        setStatus(result ? 'ready' : 'missing')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setMessage(error instanceof Error ? error.message : 'The report card is unavailable.')
        setStatus('error')
      })

    return () => controller.abort()
  }, [fips])

  if (status === 'loading') {
    return <div className="county-page">Loading county report card…</div>
  }

  if (status === 'error') {
    return (
      <div className="county-page">
        <h2>County report card</h2>
        <p className="qa-meta">{message} — is the API running?</p>
      </div>
    )
  }

  if (status === 'missing' || !card) {
    return (
      <div className="county-page">
        <h2>County report card</h2>
        <p className="qa-meta">
          No county with FIPS <code>{fips}</code>. A county code is five digits — two for the state
          and three for the county, as NBI item 3 publishes them (for example{' '}
          <Link to="/county/12086">12086</Link> for Miami-Dade). A county that exists but publishes
          no structures returns a card with zero counts rather than this message.
        </p>
        <p className="qa-meta">
          <Link to="/rankings?groupBy=county">Browse counties by condition</Link>
        </p>
      </div>
    )
  }

  const { population, trend } = card

  return (
    <div
      className="county-page"
      role="region"
      aria-label={`Report card for ${card.countyName}`}
      tabIndex={0}
    >
      <h2>
        {card.countyName}, {card.stateName}
      </h2>
      <p className="qa-meta">
        County FIPS {card.countyFips} · snapshot {card.snapshotYear} ·{' '}
        <Link to={`/?state=${card.state}&county=${card.countyFips.slice(2)}`}>
          Show on the map
        </Link>{' '}
        · <a href={`${BASE}${card.csvUrl}`}>Download as CSV</a>
      </p>

      <div className="qa-cards">
        <Card value={card.structures.toLocaleString()} label="Structures published" />
        <Card
          value={card.poorPercent === null ? '—' : `${card.poorPercent.toFixed(1)}%`}
          label="Poor, of rated"
          poor
        />
        <Card value={card.medianYearBuilt?.toString() ?? '—'} label="Median year built" />
        <Card
          value={population.estimate === null ? 'Not published' : population.estimate.toLocaleString()}
          label="Population served"
        />
      </div>

      <h3>Condition</h3>
      {card.rated === 0 ? (
        <p className="qa-meta">
          {card.structures === 0
            ? 'This county publishes no structures in the loaded snapshot.'
            : `None of the ${card.structures.toLocaleString()} structures here carries a numeric rating on
               any governing item, so there is no condition share to state.`}
        </p>
      ) : (
        <div className="qa-table-wrap">
          <table className="qa-table">
            <caption>
              Counts from the current snapshot. Shares are of the {card.rated.toLocaleString()}{' '}
              rated structures — not of the total, because a structure FHWA did not rate is not
              evidence either way.
            </caption>
            <thead>
              <tr>
                <th scope="col">Class</th>
                <th scope="col">Structures</th>
                <th scope="col">Share of rated</th>
              </tr>
            </thead>
            <tbody>
              <ConditionRow label="Good" count={card.good} share={card.goodPercent} />
              <ConditionRow label="Fair" count={card.fair} share={card.fairPercent} />
              <ConditionRow label="Poor" count={card.poor} share={card.poorPercent} />
              <tr data-class="unrated">
                <th scope="row">Unrated</th>
                <td>{card.unrated.toLocaleString()}</td>
                {/* Not "0.0%" — an unrated structure is outside the denominator, so it has no
                    share at all, and a zero here would read as "none of them". */}
                <td>excluded from the share</td>
              </tr>
            </tbody>
          </table>
        </div>
      )}

      <p className="qa-meta">{card.countsNote}</p>

      <h3>Population served</h3>
      {population.estimate === null ? (
        <p className="qa-meta">{population.citation}</p>
      ) : (
        <p className="qa-meta">
          <strong>{population.estimate.toLocaleString()}</strong>
          {population.marginOfError !== null
            ? ` ± ${population.marginOfError.toLocaleString()}`
            : ' (margin of error not published for this county)'}{' '}
          — {population.citation}
        </p>
      )}
      <p className="qa-meta">{population.note}</p>

      <h3>Condition over time</h3>
      {trend === null || trend.points.length === 0 ? (
        <p className="qa-meta">
          No published condition history for this county. Either the FR-1.2 trend job has not been
          published, or FHWA published no structures under this county code in any vintage — which
          is the case for every Connecticut planning region, since item 3 still carries the eight
          legacy county codes the Census retired in 2022.
        </p>
      ) : (
        <>
          <div className="qa-table-wrap">
            <table className="qa-table">
              <caption>
                Published Good/Fair/Poor counts per year, {trend.fromYear}–{trend.toYear}. Years FHWA
                did not publish are absent rather than interpolated.
              </caption>
              <thead>
                <tr>
                  <th scope="col">Year</th>
                  <th scope="col">Total</th>
                  <th scope="col">Good</th>
                  <th scope="col">Fair</th>
                  <th scope="col">Poor</th>
                  <th scope="col">Unrated</th>
                  <th scope="col">Poor %</th>
                </tr>
              </thead>
              <tbody>
                {trend.points.map((p) => (
                  <tr key={p.year} data-year={p.year}>
                    <th scope="row">{p.year}</th>
                    <td>{p.total.toLocaleString()}</td>
                    <td>{p.good.toLocaleString()}</td>
                    <td>{p.fair.toLocaleString()}</td>
                    <td>{p.poor.toLocaleString()}</td>
                    <td>{p.unknown.toLocaleString()}</td>
                    <td>{p.poorShare === null ? '—' : `${p.poorShare.toFixed(1)}%`}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="qa-meta">{trend.method}</p>
          <p className="qa-meta">
            <Link to={`/trends?level=county&fips=${card.countyFips}`}>
              See this series as a chart
            </Link>
            {trend.provenance ? ` · job ${trend.provenance.jobRunId}` : ''}
          </p>
        </>
      )}

      <p className="qa-meta">{card.methodNote}</p>
    </div>
  )
}

function ConditionRow({
  label,
  count,
  share,
}: {
  label: string
  count: number
  share: number | null
}) {
  return (
    <tr data-class={label.toLowerCase()}>
      <th scope="row">{label}</th>
      <td>{count.toLocaleString()}</td>
      <td>{share === null ? '—' : `${share.toFixed(1)}%`}</td>
    </tr>
  )
}

function Card({ value, label, poor }: { value: string; label: string; poor?: boolean }) {
  return (
    <div className="kpi">
      <div className={`value${poor ? ' poor' : ''}`}>{value}</div>
      <div className="label">{label}</div>
    </div>
  )
}
