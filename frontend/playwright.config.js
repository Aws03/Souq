import { defineConfig, devices } from '@playwright/test';

// ============================================================================
// تحقّق المتصفّح للمرحلة 16 (§22) — ليس مجموعة اختبارات ثانية تُشغَّل في CI، بل تنفيذ الرحلات
// الحرجة على مكدّس حقيقي: SQL Server، الـ API، خادم Vite. يُشغَّل يدوياً مقابل بيئة قائمة:
//
//   1) docker compose up -d db
//   2) dotnet run --project src/Souq.API        (Development، المنفذ 5200)
//   3) cd frontend && npm run dev               (المنفذ 5173، يُوكّل /api)
//   4) npx playwright test
//
// المضيف هو ما يحدّد المتجر (الخادم يحلّه)، ووكيل Vite يمرّر ترويسة Host كما هي — فمتجرٌ ثانٍ
// يُزار على second.localhost:5173 ويُحَلّ فعلاً إلى متجر آخر. هذا هو معيار خروج المرحلة:
// "الرحلات الحرجة تمرّ لمتجرين".
// ============================================================================
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'en-US',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'phone', use: { ...devices['Pixel 7'] }, testMatch: /responsive\.spec\.js/ },
  ],
});
