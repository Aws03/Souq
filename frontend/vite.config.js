import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // يحوّل أي طلب /api تلقائياً إلى خادم الـ .NET (يتجنّب مشاكل CORS أثناء التطوير).
    proxy: { '/api': 'http://localhost:5000' }
  }
})
