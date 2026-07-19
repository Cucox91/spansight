import { useEffect, useRef, useState } from 'react'
import maplibregl from 'maplibre-gl'
import { Protocol } from 'pmtiles'
import { useNavigate } from 'react-router-dom'
import 'maplibre-gl/dist/maplibre-gl.css'
import { fetchGeoJson } from '../api/client'
import type { GeoJsonMeta } from '../api/types'
import { useFilters } from '../state/FiltersContext'
import {
  ALL_CONDITIONS,
  ALL_TYPE_GROUPS,
  TYPE_GROUPS,
  filtersKey,
  type FilterState,
} from '../state/filters'

const EMPTY_COLLECTION: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features: [] }

// Optional PMTiles source (FR-0.5): set VITE_TILES_URL to a published .pmtiles artifact and the
// layer switches from the API GeoJSON fallback to static vector tiles (ADR-002).
const TILES_URL: string = (import.meta.env.VITE_TILES_URL as string | undefined) ?? ''

/**
 * The map explorer core: MapLibre canvas + condition-colored bridge layer driven by the shared
 * filter predicate. Clicking a point deep-links to /bridge/{state}/{structureNumber} (UC-0.3).
 */
export default function BridgeMap() {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const [mapReady, setMapReady] = useState(false)
  const [basemapFailed, setBasemapFailed] = useState(false)
  // Tiles mode renders progressively from the PMTiles source and never runs the fallback
  // effect below — the only place this flag is ever cleared — so it must start false there.
  const [loadingData, setLoadingData] = useState(!TILES_URL)
  const [apiError, setApiError] = useState(false)
  const [meta, setMeta] = useState<GeoJsonMeta | null>(null)
  const { filters } = useFilters()
  const navigate = useNavigate()
  const key = filtersKey(filters)

  useEffect(() => {
    if (!containerRef.current) {
      return
    }

    const protocol = new Protocol()
    maplibregl.addProtocol('pmtiles', protocol.tile)

    let map: maplibregl.Map
    try {
      map = new maplibregl.Map({
        container: containerRef.current,
        style: 'https://tiles.openfreemap.org/styles/liberty',
        center: [-95.5, 38.6],
        zoom: 3.8,
        attributionControl: false, // attribution rendered in the persistent footer (NFR-8)
      })
    } catch {
      setBasemapFailed(true)
      return () => maplibregl.removeProtocol('pmtiles')
    }

    mapRef.current = map
    map.addControl(new maplibregl.NavigationControl(), 'bottom-right')

    map.on('load', () => {
      if (TILES_URL) {
        map.addSource('bridges', { type: 'vector', url: `pmtiles://${TILES_URL}` })
        map.addLayer({
          id: 'bridge-points',
          type: 'circle',
          source: 'bridges',
          'source-layer': 'bridges',
          paint: paintSpec(),
        })
      } else {
        map.addSource('bridges', { type: 'geojson', data: EMPTY_COLLECTION })
        map.addLayer({ id: 'bridge-points', type: 'circle', source: 'bridges', paint: paintSpec() })
      }

      map.on('click', 'bridge-points', (event) => {
        const feature = event.features?.[0]
        const id = feature?.properties?.id as string | undefined
        if (id && id.includes('-')) {
          const [state, ...rest] = id.split('-')
          navigate(`/bridge/${state}/${encodeURIComponent(rest.join('-'))}`)
        }
      })
      map.on('mouseenter', 'bridge-points', () => {
        map.getCanvas().style.cursor = 'pointer'
      })
      map.on('mouseleave', 'bridge-points', () => {
        map.getCanvas().style.cursor = ''
      })

      setMapReady(true)
    })

    map.on('error', () => {
      if (!map.isStyleLoaded()) {
        setBasemapFailed(true)
      }
    })

    return () => {
      maplibregl.removeProtocol('pmtiles')
      map.remove()
      mapRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Tiles path: the PMTiles archive is the static national set, so the shared predicate
  // applies client-side as a layer filter over the minified tile schema (tools/build-tiles.sh).
  useEffect(() => {
    if (!TILES_URL || !mapReady) {
      return
    }
    mapRef.current?.setFilter('bridge-points', tileFilter(filters))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, mapReady])

  // GeoJSON fallback path: refetch on every predicate change; abort superseded requests.
  useEffect(() => {
    if (TILES_URL || !mapReady) {
      return
    }

    const controller = new AbortController()
    setLoadingData(true)
    fetchGeoJson(filters, controller.signal)
      .then((collection) => {
        setApiError(false)
        setMeta(collection.meta)
        const source = mapRef.current?.getSource('bridges') as maplibregl.GeoJSONSource | undefined
        source?.setData(collection as never)
      })
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setApiError(true)
        }
      })
      .finally(() => setLoadingData(false))

    return () => controller.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, mapReady])

  return (
    <>
      <div ref={containerRef} className="map-canvas" aria-label="Bridge map" />
      {loadingData && !basemapFailed && <div className="skeleton">Loading bridges…</div>}
      {meta?.truncated && (
        <div className="map-note">
          Showing {meta.returned.toLocaleString()} of {meta.total.toLocaleString()} — filter or zoom
          for the full set (vector tiles land with FR-0.5).
        </div>
      )}
      {apiError && <div className="map-error-toast">Bridge data unavailable — is the API running?</div>}
      {basemapFailed && (
        <div className="map-fallback">
          Basemap failed to load (offline?).
          <br />
          Filters, KPIs, and the drawer keep working against the API.
        </div>
      )}
    </>
  )
}

/**
 * FilterState → MapLibre filter over tile properties, mirroring toSearchParams semantics:
 * all-selected dimensions stay unconstrained; bounded year/ADT exclude features missing the
 * property, matching the API's SQL comparisons against NULL.
 */
function tileFilter(filters: FilterState): maplibregl.FilterSpecification | null {
  const clauses: unknown[] = []

  if (filters.conditions.length > 0 && filters.conditions.length < ALL_CONDITIONS.length) {
    clauses.push(['in', ['get', 'cond'], ['literal', filters.conditions]])
  }

  if (filters.state) {
    clauses.push(['==', ['get', 'state'], filters.state])
  }

  if (filters.typeGroups.length > 0 && filters.typeGroups.length < ALL_TYPE_GROUPS.length) {
    const designs = filters.typeGroups.flatMap((group) => [...TYPE_GROUPS[group]])
    clauses.push(['in', ['get', 'design'], ['literal', designs]])
  }

  if (filters.yearBuiltMax !== null) {
    clauses.push(['<=', ['coalesce', ['get', 'year'], 99999], filters.yearBuiltMax])
  }

  if (filters.minAdt > 0) {
    clauses.push(['>=', ['coalesce', ['get', 'adt'], -1], filters.minAdt])
  }

  return clauses.length > 0 ? (['all', ...clauses] as unknown as maplibregl.FilterSpecification) : null
}

function paintSpec(): maplibregl.CircleLayerSpecification['paint'] {
  return {
    'circle-radius': ['interpolate', ['linear'], ['zoom'], 3, 4.5, 10, 12],
    // Good/Fair/Poor tokens from docs/DESIGN.md; Unknown falls back to muted. The API GeoJSON
    // names the property conditionClass; the PMTiles pipeline ships it minified as cond.
    'circle-color': [
      'match',
      ['coalesce', ['get', 'conditionClass'], ['get', 'cond']],
      'Good',
      '#2e7d32',
      'Fair',
      '#e6a817',
      'Poor',
      '#c62828',
      '#5c6b7a',
    ],
    'circle-opacity': 0.85,
    'circle-stroke-width': 1.2,
    'circle-stroke-color': '#ffffff',
  }
}
