import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // يحوّل أي طلب /api تلقائياً إلى خادم الـ .NET (يتجنّب مشاكل CORS أثناء التطوير).
    // ملاحظة 1: المنفذ 5200 وليس 5000 — macOS يحجز 5000 لخدمة AirPlay.
    // ملاحظة 2: 127.0.0.1 وليس localhost — حلّ localhost داخل Node قد يعلّق الوكيل.
    // ملاحظة 3: صيغة الكائن وليست النص المختصر — المختصرة علّقت الوكيل على هذا الجهاز.
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5200',
        configure(proxy) {
          proxy.on('error', (e) => console.error('[api-proxy]', e.message));
        },
      },
      // صور المنتجات المرفوعة (مرحلة 3ب) يخدمها الـ API على /uploads مباشرة —
      // بلا هذا الوكيل ستُطلب من خادم Vite نفسه فتُرجع 404 في التطوير.
      '/uploads': {
        target: 'http://127.0.0.1:5200',
        configure(proxy) {
          proxy.on('error', (e) => console.error('[uploads-proxy]', e.message));
        },
      },
    }
  }
})
