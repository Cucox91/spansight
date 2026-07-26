import type { BridgeHistory, ConditionPoint } from '../api/types'

const WIDTH = 300
const HEIGHT = 64
const PAD_X = 4
const PAD_Y = 8
// NBI condition ratings run 0–9; fixing the axis means two bridges' sparklines are comparable and
// a flat line reads as "unchanged", not as "no range in this particular bridge".
const MIN_RATING = 0
const MAX_RATING = 9

function x(year: number, first: number, last: number): number {
  if (last === first) {
    return WIDTH / 2
  }
  return PAD_X + ((year - first) / (last - first)) * (WIDTH - 2 * PAD_X)
}

function y(rating: number): number {
  const t = (rating - MIN_RATING) / (MAX_RATING - MIN_RATING)
  return HEIGHT - PAD_Y - t * (HEIGHT - 2 * PAD_Y)
}

/**
 * Splits the history into runs of consecutive published years. Each run is drawn as its own
 * polyline so a year FHWA did not publish leaves a visible break — the chart never draws a line
 * across a gap, because that line would be an interpolation nobody published (GR-6).
 */
function segments(points: ConditionPoint[]): ConditionPoint[][] {
  const runs: ConditionPoint[][] = []
  let current: ConditionPoint[] = []
  for (const point of points) {
    const previous = current[current.length - 1]
    if (previous && point.year !== previous.year + 1) {
      runs.push(current)
      current = []
    }
    if (point.lowestRating == null) {
      // Published, but with no numeric rating — cannot sit on a 0–9 axis, so it breaks the line
      // rather than being drawn at some invented height.
      if (current.length > 0) {
        runs.push(current)
        current = []
      }
      continue
    }
    current.push(point)
  }
  if (current.length > 0) {
    runs.push(current)
  }
  return runs
}

/**
 * FR-1.2 AC-2 — the drawer's condition history: lowest published NBI rating per year.
 * Rendered as inline SVG (no chart library: this is a polyline and four circles) with a table
 * fallback for assistive technology, so the data is reachable without reading the graphic.
 */
export default function ConditionSparkline({ history }: { history: BridgeHistory }) {
  const { points, firstYear, lastYear, observedYears } = history
  const rated = points.filter((p) => p.lowestRating != null)
  const spanYears = lastYear - firstYear + 1
  const gaps = spanYears - observedYears

  if (rated.length === 0) {
    return (
      <div className="spark-empty">
        This structure appears in {observedYears} NBI vintage{observedYears === 1 ? '' : 's'}{' '}
        ({firstYear}–{lastYear}) but none published a numeric condition rating, so there is no
        history to chart.
      </div>
    )
  }

  const runs = segments(points)
  const first = points[0].year
  const last = points[points.length - 1].year
  const summary =
    `Lowest published NBI condition rating per year, ${firstYear} to ${lastYear}. ` +
    rated.map((p) => `${p.year}: ${p.lowestRating} (${p.conditionClass})`).join('. ') +
    '.'

  return (
    <div className="spark">
      <svg
        className="spark-svg"
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        role="img"
        aria-label={summary}
        preserveAspectRatio="none"
      >
        {/* Good/Fair and Fair/Poor thresholds, so a point's colour is anchored to a visible scale. */}
        {[7, 5].map((threshold) => (
          <line
            key={threshold}
            className="spark-threshold"
            x1={PAD_X}
            x2={WIDTH - PAD_X}
            y1={y(threshold)}
            y2={y(threshold)}
          />
        ))}
        {runs.map((run) => (
          <polyline
            key={`run-${run[0].year}`}
            className="spark-line"
            points={run.map((p) => `${x(p.year, first, last)},${y(p.lowestRating as number)}`).join(' ')}
          />
        ))}
        {rated.map((p) => (
          <circle
            key={p.year}
            className={`spark-dot ${p.conditionClass.toLowerCase()}`}
            cx={x(p.year, first, last)}
            cy={y(p.lowestRating as number)}
            r={2.5}
          />
        ))}
      </svg>

      <div className="spark-axis">
        <span>{firstYear}</span>
        <span>{lastYear}</span>
      </div>

      {/* The numbers themselves, for screen readers and for anyone who wants the values. Kept in
          the DOM rather than behind a toggle so nothing depends on reading the SVG. */}
      <details className="spark-values">
        <summary>Show the {observedYears} published rating{observedYears === 1 ? '' : 's'}</summary>
        <table className="attrs">
          <thead>
            <tr>
              <th scope="col">Year</th>
              <th scope="col">Lowest rating</th>
              <th scope="col">Condition</th>
            </tr>
          </thead>
          <tbody>
            {points.map((p) => (
              <tr key={p.year}>
                <td>{p.year}</td>
                <td>{p.lowestRating ?? '—'}</td>
                <td>{p.conditionClass}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>

      {gaps > 0 && (
        <p className="spark-note">
          {gaps} year{gaps === 1 ? '' : 's'} in {firstYear}–{lastYear} published no record for this
          structure; those years are left blank rather than estimated.
        </p>
      )}
    </div>
  )
}
