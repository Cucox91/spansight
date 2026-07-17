import { useEffect, useState } from 'react'
import { fetchQaSummary } from '../api/client'
import type { QaSummary } from '../api/types'

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
      </div>
    )
  }

  const maxReason = Math.max(1, ...summary.byReason.map((r) => r.count))
  const maxState = Math.max(1, ...summary.byState.map((s) => s.count))

  return (
    <div className="qa-page">
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
    </div>
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
