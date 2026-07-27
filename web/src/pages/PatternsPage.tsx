import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { fetchDeteriorationCatalog, fetchDeteriorationMatrix } from '../api/client'
import type {
  CohortGroup,
  ConditionComponent,
  DeteriorationCatalog,
  DeteriorationMatrix,
  DeteriorationMatrixRow,
} from '../api/types'

const RATINGS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]

// --poor and --good from index.css, as RGB so the shade can be mixed with an alpha.
const DECLINE_RGB = '198, 40, 40'
const IMPROVE_RGB = '46, 125, 50'

/**
 * FR-1.3 AC-4 — cohort-level condition-transition patterns.
 *
 * How often bridges in a cohort were published at one NBI rating one year and another the next,
 * counted across consecutive vintages. **Cohort level only** — nothing here is shown for an
 * individual structure, and nothing is predicted (GR-6). The method note, the link to
 * `docs/METHODOLOGY-DETERIORATION.md`, the inspection-cadence caveat and the sample-size floor all
 * come from the API and render adjacent to the matrix, so a grid cannot appear without them.
 *
 * The whole query lives in the URL (component + cohort), so a view is shareable and reload-safe the
 * same way the trends view and the bridge drawer are (UC-0.3).
 */
export default function PatternsPage() {
  const [params, setParams] = useSearchParams()
  const component = (params.get('component') ?? 'Deck') as ConditionComponent
  const typeGroup = params.get('typeGroup')
  const materialGroup = params.get('materialGroup')
  const region = params.get('region')
  const cohort =
    typeGroup && materialGroup && region ? { typeGroup, materialGroup, region } : null

  const [catalog, setCatalog] = useState<DeteriorationCatalog | null>(null)
  const [matrix, setMatrix] = useState<DeteriorationMatrix | null>(null)
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const [message, setMessage] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    fetchDeteriorationCatalog(controller.signal)
      .then(setCatalog)
      .catch(() => {
        /* The pickers degrade; the matrix does not depend on the catalog. */
      })
    return () => controller.abort()
  }, [])

  const cohortKey = cohort ? `${cohort.typeGroup}|${cohort.materialGroup}|${cohort.region}` : ''
  useEffect(() => {
    const controller = new AbortController()
    setStatus('loading')
    fetchDeteriorationMatrix(component, cohort, controller.signal)
      .then((data) => {
        setMatrix(data)
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [component, cohortKey])

  const update = (next: Record<string, string | null>) => {
    const merged = new URLSearchParams(params)
    for (const [key, value] of Object.entries(next)) {
      if (value === null) merged.delete(key)
      else merged.set(key, value)
    }
    setParams(merged, { replace: false })
  }

  const selectCohort = (value: string) => {
    if (value === '') {
      update({ typeGroup: null, materialGroup: null, region: null })
      return
    }
    const [nextType, nextMaterial, nextRegion] = value.split('|')
    update({ typeGroup: nextType, materialGroup: nextMaterial, region: nextRegion })
  }

  return (
    <div className="patterns-page">
      <h2>Condition-transition patterns</h2>
      <p className="qa-meta">
        How often bridges in a cohort were published at one NBI condition rating in a given year and
        another the next, counted across consecutive vintages. These are counts of published
        inspection ratings for a <strong>group</strong> of bridges — never a statement about any
        individual structure, and never a projection of what any bridge will do next.
      </p>

      <form className="trends-controls" onSubmit={(event) => event.preventDefault()}>
        <label>
          <span>Component</span>
          <select value={component} onChange={(event) => update({ component: event.target.value })}>
            {(catalog?.dimensions.components ?? ['Deck']).map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </label>

        <label>
          <span>Cohort</span>
          <select value={cohortKey} onChange={(event) => selectCohort(event.target.value)}>
            <option value="">All bridges (national context)</option>
            {catalog?.cohorts
              .filter((c) => !c.isNational)
              .map((c) => {
                const key = `${c.typeGroup}|${c.materialGroup}|${c.region}`
                const pairs = c.components.find((x) => x.component === component)?.pairs ?? 0
                return (
                  <option key={key} value={key}>
                    {c.typeGroup} · {c.materialGroup} · {c.region} ({pairs.toLocaleString()})
                  </option>
                )
              })}
          </select>
        </label>
      </form>

      {status === 'loading' && <div className="drawer-state">Loading transition matrix…</div>}
      {status === 'error' && (
        <div className="drawer-state" role="alert">
          Could not load this matrix — {message}
        </div>
      )}

      {status === 'ready' && matrix && <Matrix matrix={matrix} catalog={catalog} />}
    </div>
  )
}

/** The published codes a group collapses, so a label like "Other" is never left to imply something. */
function groupCodes(groups: CohortGroup[] | undefined, name: string): string {
  const group = groups?.find((g) => g.name === name)
  if (!group || group.codes.length === 0) return ''
  return group.codes.map((c) => `${c.code} ${c.label}`).join(' · ')
}

function Matrix({
  matrix,
  catalog,
}: {
  matrix: DeteriorationMatrix
  catalog: DeteriorationCatalog | null
}) {
  const { cohort, rows, sampleFloor } = matrix

  // The diagonal carries 91–93% of every real cohort, so putting it on the same colour scale as the
  // off-diagonal renders one bright stripe and nine dark rows — a picture that says nothing. The
  // scale is therefore computed over changed transitions only, and the diagonal gets its own
  // treatment. Counts stay visible in every cell either way (methodology §4).
  //
  // Declines and improvements are shaded in different hues rather than one "changed" ramp: a single
  // red ramp would colour a 4→8 rehabilitation the same as a 8→4 fall, which is the kind of implied
  // judgment GR-6 rules out.
  const maxChangedRate = useMemo(() => {
    let max = 0
    for (const row of rows) {
      if (!row.sufficient) continue
      for (const cell of row.cells) {
        if (cell.toRating !== row.fromRating && cell.rate != null) max = Math.max(max, cell.rate)
      }
    }
    return max || 1
  }, [rows])

  const label = cohort.isNational
    ? 'All bridges (national context)'
    : `${cohort.typeGroup} · ${cohort.materialGroup} · ${cohort.region}`

  return (
    <div
      className="matrix-result"
      role="region"
      aria-label={`Condition-transition patterns for ${label}, ${matrix.component}`}
    >
      <h3>
        {matrix.component} · {label}
      </h3>
      <p className="qa-meta">
        {matrix.pairs.toLocaleString()} transition{matrix.pairs === 1 ? '' : 's'} observed
        {matrix.provenance
          ? ` across ${matrix.provenance.yearPairs} consecutive vintage pairs, ${matrix.provenance.firstYear}–${matrix.provenance.lastYear}`
          : ''}
        .{' '}
        {matrix.rowsBelowFloor === 0
          ? `Every row holds at least ${sampleFloor} transitions, so all ten publish rates.`
          : `${matrix.rowsBelowFloor} of 10 rows hold fewer than ${sampleFloor} transitions and publish counts only.`}
      </p>

      {!cohort.isNational && (
        <p className="qa-meta">
          {groupCodes(catalog?.dimensions.typeGroups, cohort.typeGroup) && (
            <>
              <strong>{cohort.typeGroup}</strong> = item 43B{' '}
              {groupCodes(catalog?.dimensions.typeGroups, cohort.typeGroup)}.{' '}
            </>
          )}
          {groupCodes(catalog?.dimensions.materialGroups, cohort.materialGroup) && (
            <>
              <strong>{cohort.materialGroup}</strong> = item 43A{' '}
              {groupCodes(catalog?.dimensions.materialGroups, cohort.materialGroup)}.
            </>
          )}
          {cohort.region === 'Outside contiguous U.S.' && (
            <>
              {' '}
              This bucket is climatically heterogeneous and its membership changes over the period —
              Alaska, Hawaii and Puerto Rico appear throughout, Guam only 2018–2024 and the U.S.
              Virgin Islands only 2019–2025.
            </>
          )}
        </p>
      )}

      {/* Wide by nature: 10 to-ratings plus the row's evidence. Focusable so keyboard users can
          scroll it (axe scrollable-region-focusable, NFR-7) — the same treatment the QA report and
          the trends view get. */}
      <div className="matrix-scroll" tabIndex={0} role="group" aria-label="Transition matrix">
        <table className="matrix">
          <caption>
            Rows are the rating published in one year; columns are the rating published the next.
            Each cell shows the number of transitions, and its share of the row where the row holds
            at least {sampleFloor}.
          </caption>
          <thead>
            <tr>
              <th scope="col">From</th>
              <th scope="col">Transitions</th>
              <th scope="col">Years</th>
              {RATINGS.map((r) => (
                <th key={r} scope="col">
                  → {r}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <MatrixRow key={row.fromRating} row={row} maxChangedRate={maxChangedRate} floor={sampleFloor} />
            ))}
          </tbody>
        </table>
      </div>

      <ul className="matrix-legend">
        <li>
          <span className="swatch diagonal" /> No change (same rating both years)
        </li>
        <li>
          <span className="swatch declined" /> A lower rating than the year before (left of the
          diagonal)
        </li>
        <li>
          <span className="swatch improved" /> A higher rating than the year before — kept exactly as
          published, never censored (right of the diagonal)
        </li>
        <li>
          Shading is relative to the largest changed share in this matrix; the counts are the figures
          to read.
        </li>
        <li>
          <span className="swatch insufficient" /> Fewer than {sampleFloor} transitions in the row:
          counts shown, no rate published
        </li>
      </ul>

      {/* GR-6 (FR-1.3 AC-4): the method, the cadence caveat and the link to the published
          methodology sit with the matrix, not in a footnote elsewhere on the site. */}
      <p className="history-method">{matrix.cadenceCaption}</p>
      <p className="history-method">
        {matrix.method}{' '}
        <a href={matrix.methodologyUrl} target="_blank" rel="noreferrer">
          Read the full methodology ({matrix.methodologyVersion})
        </a>
        .
      </p>
      {matrix.provenance && (
        <p className="qa-meta">
          Computed offline from the NBI vintage catalog · job {matrix.provenance.jobRunId} · catalog{' '}
          <code>{matrix.provenance.catalogSha256.slice(0, 12)}</code>
          {matrix.provenance.publishedUtc
            ? ` · published ${new Date(matrix.provenance.publishedUtc).toLocaleDateString()}`
            : ''}
        </p>
      )}
    </div>
  )
}

function MatrixRow({
  row,
  maxChangedRate,
  floor,
}: {
  row: DeteriorationMatrixRow
  maxChangedRate: number
  floor: number
}) {
  const span =
    row.firstFromYear == null || row.lastFromYear == null
      ? '—'
      : row.firstFromYear === row.lastFromYear
        ? `${row.firstFromYear}`
        : `${row.firstFromYear}–${row.lastFromYear}`

  return (
    <tr data-from-rating={row.fromRating} className={row.sufficient ? undefined : 'insufficient'}>
      <th scope="row">{row.fromRating}</th>
      <td className="n">
        {row.rowTotal.toLocaleString()}
        {!row.sufficient && (
          // AC-3: below the floor the row publishes counts but never a rate. The words are in the
          // DOM, not conveyed by shading alone (NFR-7).
          <small>insufficient data (n &lt; {floor})</small>
        )}
      </td>
      <td className="n">
        {span}
        {row.yearPairsObserved > 0 && (
          // All 33 year-pairs are pooled, so a row must say how much of the period it actually
          // covers — otherwise a cell built on pre-2009 coding practice reads as 34 years of
          // history (methodology §6.8).
          <small>
            {row.yearPairsObserved} vintage pair{row.yearPairsObserved === 1 ? '' : 's'}
          </small>
        )}
      </td>
      {row.cells.map((cell) => {
        const isDiagonal = cell.toRating === row.fromRating
        const intensity =
          row.sufficient && !isDiagonal && cell.rate != null
            ? Math.min(1, cell.rate / maxChangedRate)
            : 0
        // Alpha is capped at 0.35 so every shade keeps text at or above WCAG AA against --ink; a
        // deeper ramp forced a white-text flip that failed contrast at 10px in both directions.
        const alpha = intensity > 0 ? 0.06 + 0.29 * intensity : 0
        const tint = cell.toRating < row.fromRating ? DECLINE_RGB : IMPROVE_RGB
        return (
          <td
            key={cell.toRating}
            className={[
              'cell',
              isDiagonal ? 'diagonal' : '',
              row.sufficient ? '' : 'insufficient',
              cell.pairs === 0 ? 'empty' : '',
            ]
              .filter(Boolean)
              .join(' ')}
            style={alpha > 0 ? { background: `rgba(${tint}, ${alpha})` } : undefined}
          >
            <span className="count">{cell.pairs.toLocaleString()}</span>
            <span className="rate">{cell.rate == null ? '—' : `${cell.rate.toFixed(1)}%`}</span>
          </td>
        )
      })}
    </tr>
  )
}
