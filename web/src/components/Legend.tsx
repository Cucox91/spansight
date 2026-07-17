export default function Legend() {
  return (
    <div className="legend" aria-label="Condition legend">
      <div>
        <span className="chip good" /> Good (lowest rating ≥ 7)
      </div>
      <div>
        <span className="chip fair" /> Fair (5–6)
      </div>
      <div>
        <span className="chip poor" /> Poor (≤ 4)
      </div>
    </div>
  )
}
