import { Route, Routes } from 'react-router-dom'
import AppShell from './components/AppShell'
import CountyPage from './pages/CountyPage'
import ExplorerPage from './pages/ExplorerPage'
import PatternsPage from './pages/PatternsPage'
import QaPage from './pages/QaPage'
import RankingsPage from './pages/RankingsPage'
import TrendsPage from './pages/TrendsPage'
import { FiltersProvider } from './state/FiltersContext'

function App() {
  return (
    <FiltersProvider>
      <AppShell>
        <Routes>
          <Route path="/" element={<ExplorerPage />} />
          <Route path="/bridge/:state/:structureNumber" element={<ExplorerPage />} />
          <Route path="/trends" element={<TrendsPage />} />
          <Route path="/patterns" element={<PatternsPage />} />
          <Route path="/rankings" element={<RankingsPage />} />
          <Route path="/county/:fips" element={<CountyPage />} />
          <Route path="/qa" element={<QaPage />} />
        </Routes>
      </AppShell>
    </FiltersProvider>
  )
}

export default App
