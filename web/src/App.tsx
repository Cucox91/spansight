import './App.css'
import BridgeMap from './components/BridgeMap'

function App() {
  return (
    <div className="app">
      <header className="app-header">
        <h1>SpanSight</h1>
        <p>National bridge-inventory explorer — FHWA NBI public data</p>
      </header>
      <main className="app-main">
        <BridgeMap />
      </main>
      {/* GR-6 (docs/REQUIREMENTS.md §2): this disclaimer footer must remain in the UI. */}
      <footer className="app-footer">
        <p>
          Independent personal project built on public federal and county data. Displays
          published inventory data only — not engineering advice. Not affiliated with or
          endorsed by any Department of Transportation.
        </p>
      </footer>
    </div>
  )
}

export default App
