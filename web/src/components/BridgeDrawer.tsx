import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { fetchDetail, fetchHistory } from '../api/client'
import type { BridgeDetail, BridgeHistory } from '../api/types'
import ConditionSparkline from './ConditionSparkline'

/**
 * Route-driven detail drawer (UC-0.3): /bridge/{state}/{structureNumber} is shareable and
 * reload-safe. Esc or × returns to the explorer with filters intact.
 */
export default function BridgeDrawer() {
  const { state = '', structureNumber = '' } = useParams()
  const navigate = useNavigate()
  const [detail, setDetail] = useState<BridgeDetail | null>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  // The history is a second, independent fetch (FR-1.2): the record renders as soon as it arrives
  // and the chart fills in after, so a missing or slow aggregate never holds up the drawer.
  const [history, setHistory] = useState<BridgeHistory | null>(null)
  const [historyStatus, setHistoryStatus] = useState<'loading' | 'ready' | 'none' | 'error'>('loading')

  useEffect(() => {
    const controller = new AbortController()
    setStatus('loading')
    fetchDetail(state, structureNumber, controller.signal)
      .then((data) => {
        setDetail(data)
        setStatus('ready')
      })
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setStatus('error')
        }
      })
    return () => controller.abort()
  }, [state, structureNumber])

  useEffect(() => {
    const controller = new AbortController()
    setHistoryStatus('loading')
    setHistory(null)
    fetchHistory(state, structureNumber, controller.signal)
      .then((data) => {
        setHistory(data)
        setHistoryStatus(data ? 'ready' : 'none')
      })
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setHistoryStatus('error')
        }
      })
    return () => controller.abort()
  }, [state, structureNumber])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        navigate('/')
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [navigate])

  const close = () => navigate('/')

  return (
    <section className="drawer" aria-label="Bridge detail">
      <div className="drawer-head">
        <div>
          <h3>{detail ? detail.facilityCarried ?? detail.id : 'Loading…'}</h3>
          <div className="sub">
            {detail
              ? `NBI ${detail.structureNumber} · County FIPS ${detail.countyCode ?? '—'} · ${detail.stateName}`
              : `${state}-${structureNumber}`}
          </div>
        </div>
        <button className="close" onClick={close} aria-label="Close detail panel">
          ×
        </button>
      </div>

      {status === 'loading' && <div className="drawer-state">Loading bridge record…</div>}
      {status === 'error' && (
        <div className="drawer-state">
          Could not load this bridge — it may not exist. <button onClick={close}>Back to map</button>
        </div>
      )}

      {status === 'ready' && detail && (
        <div className="drawer-body">
          <span className={`badge ${detail.conditionClass.toLowerCase()}`}>
            {detail.conditionClass}
            {detail.lowestRating != null ? ` — lowest rating ${detail.lowestRating}` : ''}
          </span>

          <table className="attrs">
            <tbody>
              <Row label="Crosses" value={detail.featuresIntersected} />
              <Row label="Structure type" value={`${detail.material} · ${detail.design}`} />
              <Row
                label="Year built"
                value={detail.yearBuilt != null ? `${detail.yearBuilt} (${detail.ageYears} yrs)` : null}
              />
              <Row label="Deck (item 58)" value={ratingText(detail.deck)} />
              <Row label="Superstructure (59)" value={ratingText(detail.superstructure)} />
              <Row label="Substructure (60)" value={ratingText(detail.substructure)} />
              <Row label="Culvert (62)" value={ratingText(detail.culvert)} />
              <Row
                label="AADT"
                value={detail.adt != null ? `${detail.adt.toLocaleString()} vehicles/day` : null}
              />
              <Row
                label="Length"
                value={
                  detail.structureLengthMeters != null ? `${detail.structureLengthMeters} m` : null
                }
              />
              <Row
                label="Source"
                value={`${detail.sourceFormat === 'LegacyCodingGuide' ? 'Legacy Coding Guide' : detail.sourceFormat} · ${detail.snapshotYear} snapshot`}
              />
            </tbody>
          </table>

          <section className="history" aria-label="Condition history">
            <h4>Condition history</h4>

            {historyStatus === 'loading' && (
              <div className="drawer-state">Loading condition history…</div>
            )}
            {historyStatus === 'error' && (
              <div className="drawer-state">
                Condition history is unavailable right now — the record above is unaffected.
              </div>
            )}
            {historyStatus === 'none' && (
              <div className="drawer-state">
                No published condition history for this structure in the 1992–2025 vintages.
              </div>
            )}
            {historyStatus === 'ready' && history && (
              <>
                <ConditionSparkline history={history} />
                {/* GR-6, adjacent to the chart rather than only in the footer: the method that
                    produced these numbers travels with them. */}
                <p className="history-method">{history.method}</p>
                <p className="history-method">
                  <Link
                    to={
                      detail.countyCode
                        ? `/trends?level=county&fips=${detail.stateFips}${detail.countyCode}`
                        : `/trends?level=state&fips=${detail.stateFips}`
                    }
                  >
                    Compare with {detail.countyCode ? 'county' : 'state'}-wide condition over time →
                  </Link>
                </p>
              </>
            )}
          </section>
        </div>
      )}
    </section>
  )
}

function Row({ label, value }: { label: string; value: string | null }) {
  if (value == null) {
    return null
  }
  return (
    <tr>
      <td>{label}</td>
      <td>
        <strong>{value}</strong>
      </td>
    </tr>
  )
}

function ratingText(rating: { code: string | null; text: string }): string | null {
  if (rating.code == null) {
    return null
  }
  return rating.code === 'N' ? 'N — not applicable' : `${rating.code}/9 — ${rating.text}`
}
