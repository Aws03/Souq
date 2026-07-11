import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // يحوّل أي طلب /api تلقائياً إلى خادم الـ .NET (يتجنّب مشاكل CORS أثناء التطوير).
    // ملاحظة: 5200 وليس 5000 — macOS يحجز 5000 لخدمة AirPlay.
    proxy: { '/api': 'http://localhost:5200' }
  }
})
