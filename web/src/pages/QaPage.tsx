import { useEffect, useState } from 'react'
import { fetchQaSummary } from '../api/client'
import type { CountyJoinCoverage, QaSummary } from '../api/types'

/**
 * Data QA report (FR-0.2, UC-0.6): the latest ingestion run's split plus reject breakdowns.
 * Numbers come straight from ops.ingestion_run / quarantine tables, so they reconcile with the
 * run summary by construction.
 */
export default function QaPage() {
  const [summary, setSummary] = useState<QaSummary | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchQaSummary(controller.signal)
      .then(setSummary)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setFailed(true)
        }
      })
    return () => controller.abort()
  }, [])

  if (failed) {
    return <div className="qa-page">Data QA is unavailable — is the API running?</div>
  }

  if (!summary) {
    return <div className="qa-page">Loading data-quality report…</div>
  }

  const run = summary.latestRun
  if (!run) {
    return (
      <div className="qa-page">
        <h2>Data quality</h2>
        <p className="qa-meta">
          No completed ingestion run yet. Load a snapshot:{' '}
          <code>
            dotnet run --project src/SpanSight.Ingestion -- load --file &lt;snapshot.csv&gt;
            --snapshot-year 2025
          </code>
        </p>
        <CountyJoinSection coverage={summary.countyJoin} />
      </div>
    )
  }

  const maxReason = Math.max(1, ...summary.byReason.map((r) => r.count))
  const maxState = Math.max(1, ...summary.byState.map((s) => s.count))

  return (
    // Focusable: the report is a scrollable region of static text, so keyboard users need
    // the container itself reachable to scroll it (axe scrollable-region-focusable, NFR-7).
    <div className="qa-page" role="region" aria-label="Data quality report" tabIndex={0}>
      <h2>Data quality — latest load</h2>
      <p className="qa-meta">
        {run.sourceFile} · snapshot {run.snapshotYear} · run #{run.id} ·{' '}
        {run.completedUtc ? new Date(run.completedUtc).toLocaleString() : run.status}
      </p>

      <div className="qa-cards">
        <Card value={run.rowsRead.toLocaleString()} label="Rows read" />
        <Card value={run.rowsLoaded.toLocaleString()} label="Loaded to core" />
        <Card value={run.rowsQuarantined.toLocaleString()} label="Quarantined" poor />
        <Card value={`${(run.rejectRate * 100).toFixed(2)}%`} label="Reject rate" />
      </div>

      <h3>Rejects by reason</h3>
      {summary.byReason.map((row) => (
        <Bar key={row.reason} label={row.reason} count={row.count} max={maxReason} />
      ))}

      <h3>Rejects by state (top {summary.byState.length})</h3>
      {summary.byState.map((row) => (
        <Bar key={row.stateCode} label={row.state} count={row.count} max={maxState} />
      ))}

      <p className="qa-meta">
        Quarantined rows never enter the serving tables — every reject keeps its raw line and
        machine-readable reason codes for inspection (FR-0.2). Data quality is a feature, not a
        footnote (risk R-1).
      </p>

      <CountyJoinSection coverage={summary.countyJoin} />
    </div>
  )
}

/** Display decoding of the four cross-check outcomes. The API sends the machine token. */
const DISAGREEMENT_KIND: Record<string, string> = {
  different_county_same_state: 'Another county of the same state',
  different_state: 'Another state',
  county_not_published: 'No county code published',
}

const MISS_REASON: Record<string, string> = {
  on_county_boundary: 'On a county boundary',
  outside_all_county_polygons: 'Outside every county polygon',
}

/**
 * FR-1.5 AC-2 — the bridge→county join coverage. Sits on the QA page rather than anywhere else
 * because it is the same kind of fact as an ingestion reject: a measured share of the served
 * inventory the product could not place, itemised instead of rounded away.
 *
 * Every figure and every caveat is server-authored (`coverage.methodNote`), so a view cannot
 * render the numbers without the rule they were measured under (GR-6).
 */
function CountyJoinSection({ coverage }: { coverage: CountyJoinCoverage | null }) {
  if (!coverage) {
    // Not published is not zero coverage. Saying so keeps an unrun job distinguishable from a
    // join that matched nothing, which is the failure the measurement exists to catch.
    return (
      <>
        <h3>County join coverage</h3>
        <p className="qa-meta">
          The bridge-to-county join has not been published yet, so there is no coverage to report.
          Build and load it: <code>tools/census/join-counties.sh</code> then{' '}
          <code>dotnet run --project src/SpanSight.Ingestion -- load-county-join</code>.
        </p>
      </>
    )
  }

  const { provenance } = coverage

  return (
    <>
      <h3>County join coverage</h3>
      <div className="qa-cards">
        <Card
          value={`${coverage.structureCoveragePercent.toFixed(4)}%`}
          label="Structures matched to a county"
        />
        <Card value={coverage.unmatched.toLocaleString()} label="Quarantined misses" poor />
        <Card
          value={coverage.agreePercent === null ? '—' : `${coverage.agreePercent.toFixed(2)}%`}
          label="Agree with published county"
        />
        <Card value={coverage.counties.toLocaleString()} label="Counties published" />
      </div>

      <p className="qa-meta">
        {coverage.structuresMatched.toLocaleString()} of {coverage.structures.toLocaleString()}{' '}
        record-type-1 structures fall inside a published county polygon (
        {coverage.matched.toLocaleString()} of {coverage.bridges.toLocaleString()} counting every
        served row, including the routes published <em>under</em> a structure).{' '}
        {coverage.countiesWithoutPopulation} of the {coverage.counties.toLocaleString()} counties
        have a boundary but no ACS population estimate — an absence, not a zero.
      </p>

      <h4>Misses by reason</h4>
      <div role="region" aria-label="Join misses by reason" tabIndex={0} className="qa-table-wrap">
        <table className="qa-table">
          <caption>
            Every served row that matched no county polygon, quarantined with its reason and how far
            it fell from the nearest one. Structures are the record-type-1 subset — the rest are the
            routes published <em>under</em> a structure.
          </caption>
          <thead>
            <tr>
              <th scope="col">Reason</th>
              <th scope="col">Served rows</th>
              <th scope="col">Structures</th>
              <th scope="col">Median distance</th>
              <th scope="col">Furthest</th>
            </tr>
          </thead>
          <tbody>
            {coverage.missesByReason.map((row) => (
              <tr key={row.reason} data-reason={row.reason}>
                <th scope="row">{MISS_REASON[row.reason] ?? row.reason}</th>
                <td>{row.rows.toLocaleString()}</td>
                <td>{row.structures.toLocaleString()}</td>
                <td>{formatMetres(row.medianDistanceMeters)}</td>
                <td>{formatMetres(row.maxDistanceMeters)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <h4>Largest disagreements with the published county code</h4>
      <div
        role="region"
        aria-label="Largest county code disagreements"
        tabIndex={0}
        className="qa-table-wrap"
      >
        <table className="qa-table">
          <caption>
            Where the coordinate falls in one county and NBI item 3 names another. The top{' '}
            {coverage.largestDisagreements.length} pairs by structure count.
          </caption>
          <thead>
            <tr>
              <th scope="col">Published (item 3)</th>
              <th scope="col">Contains the coordinate</th>
              <th scope="col">Kind</th>
              <th scope="col">Served rows</th>
              <th scope="col">Structures</th>
            </tr>
          </thead>
          <tbody>
            {coverage.largestDisagreements.map((row) => (
              <tr key={`${row.nbiCountyFips ?? 'none'}-${row.countyFips}`}>
                <th scope="row">
                  {row.nbiCountyName ?? row.nbiCountyFips ?? 'not published'}
                  {row.nbiFipsInTiger === false && (
                    <span className="qa-flag"> — code retired by the Census</span>
                  )}
                </th>
                <td>{row.countyName}</td>
                <td>{DISAGREEMENT_KIND[row.kind] ?? row.kind}</td>
                <td>{row.rows.toLocaleString()}</td>
                <td>{row.structures.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {coverage.rowsUnderRetiredCodes > 0 && (
        <p className="qa-meta">
          {coverage.structuresUnderRetiredCodes.toLocaleString()} structures (
          {coverage.rowsUnderRetiredCodes.toLocaleString()} served rows) carry a county code the
          current boundary file no longer publishes. Those disagree because the published code names
          a county that no longer exists, not because the coordinate is wrong — Connecticut is the
          whole of it, where item 3 still carries the eight legacy counties the Census replaced with
          nine planning regions in 2022.
        </p>
      )}

      <p className="qa-meta">{coverage.methodNote}</p>
      <p className="qa-meta">
        Job {provenance.jobRunId} · method {provenance.methodVersion} ·{' '}
        {provenance.containmentPredicate} · catalog {provenance.catalogSha256.slice(0, 12)}…
        {provenance.completedUtc
          ? ` · published ${new Date(provenance.completedUtc).toLocaleString()}`
          : ''}
      </p>
    </>
  )
}

function formatMetres(value: number | null) {
  if (value === null) {
    // The job stopped looking, which is not the same as "no county nearby".
    return 'beyond search radius'
  }

  return value >= 1000 ? `${(value / 1000).toFixed(1)} km` : `${value.toLocaleString()} m`
}

function Card({ value, label, poor }: { value: string; label: string; poor?: boolean }) {
  return (
    <div className="kpi">
      <div className={`value${poor ? ' poor' : ''}`}>{value}</div>
      <div className="label">{label}</div>
    </div>
  )
}

function Bar({ label, count, max }: { label: string; count: number; max: number }) {
  return (
    <div className="bar-row">
      <span>{label}</span>
      <div className="bar-track">
        <div className="bar-fill" style={{ width: `${(100 * count) / max}%` }} />
      </div>
      <span className="bar-count">{count.toLocaleString()}</span>
    </div>
  )
}
