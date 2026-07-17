import { Route, Routes } from 'react-router-dom'
import AppShell from './components/AppShell'
import ExplorerPage from './pages/ExplorerPage'
import QaPage from './pages/QaPage'
import { FiltersProvider } from './state/FiltersContext'

function App() {
  return (
    <FiltersProvider>
      <AppShell>
        <Routes>
          <Route path="/" element={<ExplorerPage />} />
          <Route path="/bridge/:state/:structureNumber" element={<ExplorerPage />} />
          <Route path="/qa" element={<QaPage />} />
        </Routes>
      </AppShell>
    </FiltersProvider>
  )
}

export default App
