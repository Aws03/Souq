import { defineConfig, devices } from '@playwright/test';

// ============================================================================
// تحقّق المتصفّح للمرحلة 16 (§22) — ليس مجموعة اختبارات ثانية تُشغَّل في CI، بل تنفيذ الرحلات
// الحرجة على مكدّس حقيقي: SQL Server، الـ API، خادم Vite. يُشغَّل يدوياً مقابل بيئة قائمة:
//
//   1) SQL Server على localhost,1433 (قاعدة docker compose لا تنشر منفذاً — انظر DevelopmentGuide §1)
//   2) dotnet run --project src/Souq.API        (Development، المنفذ 5200، وسجلّه في ملف لـ SOUQ_API_LOG)
//   3) cd frontend && npm run dev               (المنفذ 5173، يُوكّل /api)
//   4) npx playwright test e2e/<ملف>.spec.js --project=desktop   — ملفاً ملفاً بفاصل دقيقة (حدّ الدخول 10/دقيقة)
// الدليل الكامل: docs/09-OPERATIONS/DeveloperQualityGates.md
//
// SOUQ_E2E_BASE_URL يوجّه الرحلات إلى مكدّس آخر — أُضيف في M3 كي تُشغَّل على **حزمة الحاويات** لا على خادم
// التطوير وحده. الاثنان بيئتا تشغيل مختلفتان (وسائط Production، ترويسات nginx، فحص صحّة الحاوية)، وعطلٌ قد
// يوجد في إحداهما دون الأخرى — وهو ما تطلبه سياسة Docker في SouqMasterPlan.md §3 صراحةً.
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/search.spec.js --project=desktop
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
    baseURL: process.env.SOUQ_E2E_BASE_URL || 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'en-US',
  },
  projects: [
    // فحوص الهاتف لمشروع الهاتف وحده: تشغيلها على سطح المكتب يفحص تخطيطاً لا وجود له هناك
    // (تبديل اللغة مثلاً في الشريط العلوي لا في القائمة المنسدلة) فتسقط بلا عيب حقيقي.
    { name: 'desktop', use: { ...devices['Desktop Chrome'] }, testIgnore: /responsive\.spec\.js/ },
    { name: 'phone', use: { ...devices['Pixel 7'] }, testMatch: /responsive\.spec\.js/ },
  ],
});
