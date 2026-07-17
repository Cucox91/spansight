import { useParams } from 'react-router-dom'
import BridgeDrawer from '../components/BridgeDrawer'
import BridgeMap from '../components/BridgeMap'
import FilterRail from '../components/FilterRail'
import KpiStrip from '../components/KpiStrip'
import Legend from '../components/Legend'

/**
 * The Phase 0 map explorer (FR-0.4): rail → predicate → map + KPIs, drawer on deep link.
 * Layout per DESIGN.md — the map is the product, chrome stays out of the way.
 */
export default function ExplorerPage() {
  const { state, structureNumber } = useParams()
  const drawerOpen = Boolean(state && structureNumber)

  return (
    <>
      <FilterRail />
      <div className="map-wrap">
        <KpiStrip />
        <BridgeMap />
        <Legend />
        {drawerOpen && <BridgeDrawer />}
      </div>
    </>
  )
}
