import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Local API (src/SpanSight.Api launchSettings) — keeps the SPA same-origin in dev.
    proxy: {
      '/api': 'http://localhost:5194',
    },
  },
})
