import { NavLink } from 'react-router-dom'
import type { ReactNode } from 'react'
import AskTheMap from './AskTheMap'

export default function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="app">
      <header className="app-header">
        <div className="logo">
          Span<span>Sight</span>
        </div>
        <nav aria-label="Primary">
          <NavLink to="/" end className={({ isActive }) => (isActive ? 'active' : '')}>
            Explorer
          </NavLink>
          {/* Disabled tabs advertise later phases instead of hiding them (DESIGN.md). */}
          <button disabled title="Phase 1">
            Analytics<small>Phase 1</small>
          </button>
          <button disabled title="Phase 2">
            Live Ops<small>Phase 2</small>
          </button>
          <NavLink to="/qa" className={({ isActive }) => (isActive ? 'active' : '')}>
            Data QA
          </NavLink>
        </nav>
        <div className="spacer" />
        {/* FR-AI.1 — while Ai:Enabled is false the API answers 503 and the box explains itself. */}
        <AskTheMap />
      </header>

      <main className="app-main">{children}</main>

      {/* GR-6 (docs/REQUIREMENTS.md §2): this disclaimer footer must remain in the UI. */}
      <footer className="app-footer">
        <span>
          <strong>Independent personal project.</strong> Public FHWA NBI data — displays published
          inventory values only, not engineering advice. No affiliation with or endorsement by any
          Department of Transportation.
        </span>
        <span className="attribution">
          Basemap © <a href="https://openfreemap.org">OpenFreeMap</a> · OpenMapTiles · OpenStreetMap
          contributors
        </span>
      </footer>
    </div>
  )
}
