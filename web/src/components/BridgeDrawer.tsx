import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { fetchDetail } from '../api/client'
import type { BridgeDetail } from '../api/types'

/**
 * Route-driven detail drawer (UC-0.3): /bridge/{state}/{structureNumber} is shareable and
 * reload-safe. Esc or × returns to the explorer with filters intact.
 */
export default function BridgeDrawer() {
  const { state = '', structureNumber = '' } = useParams()
  const navigate = useNavigate()
  const [detail, setDetail] = useState<BridgeDetail | null>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')

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

          <div className="phase-note">
            <strong>Condition history</strong> — the per-bridge trend chart ships in Phase 1
            (FR-1.2), computed offline from 30 years of NBI vintages (ADR-005).
          </div>
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
