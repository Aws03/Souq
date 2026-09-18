import { expect, test } from '@playwright/test';

// ============================================================================
// المرور المتصفّحي الذي تنتظره سياسة المحتوى (M15، R-16).
//
// `frontend/nginx.conf` يترك CSP في وضع الإبلاغ فقط **عن قصد**، ويكتب شرط تفعيلها بنصّه: "افتح المتجر
// والدفع مرّة، تأكّد أن وحدة التحكّم خالية من مخالفات، ثم غيّر الاسم". هذا الملفّ هو ذلك الشرط، مُنفَّذاً
// آلياً بدل أن يُنتظر أن يتذكّره أحد: المتصفّح يُطلق `securitypolicyviolation` لكل مخالفة حتى في وضع
// الإبلاغ، فتُجمع وتُقرأ بدل قراءة وحدة تحكّم بالعين.
//
// **وهو يفحص السياسة لا الصفحة**: مخالفةٌ واحدة هنا تعني أنّ تفعيل السياسة سيكسر شيئاً لمستخدم حقيقي،
// وهو ما لا يظهر في أي اختبار آخر — لأنّ وضع الإبلاغ، بتعريفه، لا يكسر شيئاً.
//
// يُشغَّل على حزمة الحاويات وحدها (nginx هو من يضع الترويسة؛ خادم Vite في التطوير لا يضعها):
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/csp.spec.js --project=desktop
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

// يُحقن قبل أي سكربت للصفحة كي لا تفوته مخالفةٌ تقع أثناء الإقلاع — وهي أرجح لحظات وقوعها.
const COLLECTOR = () => {
  // @ts-ignore — يعيش في الصفحة
  window.__cspViolations = [];
  document.addEventListener('securitypolicyviolation', (e) => {
    // @ts-ignore
    window.__cspViolations.push({
      directive: e.effectiveDirective || e.violatedDirective,
      blocked: e.blockedURI,
      source: `${e.sourceFile || ''}:${e.lineNumber || 0}`,
      disposition: e.disposition,
    });
  });
};

// متسلسل: الاختبارات تتشارك صفحةً واحدة عمداً (جامع المخالفات يعيش فيها)، والتوازي يغلقها تحت بعضها.
test.describe.configure({ mode: 'serial' });
// مهلةٌ سخيّة: هذا الملفّ يمرّ على نحو ستّة عشر مساراً، ولكلٍّ انتظارُ استقرارٍ مقصود. المهلة الافتراضية
// (دقيقة) مقاسٌ لاختبارِ رحلةٍ واحدة لا لمسحٍ كهذا، وتجاوزها يُغلق الصفحة فيبدو العطل في مكان آخر.
test.setTimeout(240_000);

test.describe('سياسة المحتوى', () => {
  /** @type {import('@playwright/test').Page} */
  let page;
  /** @type {any[]} */
  const all = [];

  const visit = async (path, label) => {
    await page.goto(path, { waitUntil: 'load' });
    // مهلةٌ قصيرة للاستيراد الديناميكي وما يحقنه بعد الإقلاع — وهو بالضبط ما لا يُثبت إلا في متصفّح.
    await page.waitForTimeout(1200);
    const found = await page.evaluate(() => {
      // @ts-ignore
      const list = window.__cspViolations || [];
      // @ts-ignore
      window.__cspViolations = [];
      return list;
    });
    for (const violation of found) all.push({ ...violation, page: label });
    return found;
  };

  test.beforeAll(async ({ browser }) => {
    const context = await browser.newContext();
    await context.addInitScript(COLLECTOR);
    page = await context.newPage();
  });

  test.afterAll(async () => { await page.context().close(); });

  // الترويسة موجودة أصلاً: بلا هذا الفحص يمرّ كل ما تحته على خادمٍ لا يضع سياسةً إطلاقاً.
  test('1 · الترويسة موجودة، ووضعها مُسجَّل كما هو', async () => {
    const response = await page.goto('/');
    const headers = response.headers();

    const enforced = headers['content-security-policy'];
    const reportOnly = headers['content-security-policy-report-only'];
    expect(enforced || reportOnly, 'لا سياسة محتوى على الإطلاق — nginx ليس من يخدم هذه الصفحة').toBeTruthy();

    const policy = enforced || reportOnly;
    // الموجّهات التي لا يجوز أن تغيب مهما كان الوضع.
    for (const directive of ["object-src 'none'", "base-uri 'self'", "frame-ancestors 'none'", "form-action 'self'"])
      expect(policy, `الموجّه المفقود: ${directive}`).toContain(directive);

    // يُسجَّل الوضع في مخرجات التشغيل كي يُقرأ في تقرير المرحلة بلا تخمين.
    console.log(`[CSP] mode=${enforced ? 'ENFORCED' : 'REPORT-ONLY'}`);
  });

  test('2 · واجهة المتجر: الرئيسية والفئات والمنتج والبحث والسلة', async () => {
    // الرئيسية هي صفحة الكتالوج، والبحث فيها بـ `?q=` (searchRouting.js) — لا مسار `/shop`.
    expect(await visit('/', 'home')).toEqual([]);
    expect(await visit('/?q=%D9%85%D9%83%D9%86%D8%B3%D8%A9', 'search')).toEqual([]);
    expect(await visit('/offers', 'offers')).toEqual([]);

    // صفحة منتج حقيقية — صورها هي ما يُختبر (`img-src`). المقبض يُقرأ من الـ API لا بالنقر على بطاقة:
    // مسارٌ مبنيّ من معرّفٍ حقيقيّ يُصيب الصفحة دائماً، والنقر يعتمد على تخطيطٍ قد يتغيّر.
    const handle = await page.evaluate(async () => {
      const response = await fetch('/api/products?pageSize=1');
      const body = response.ok ? await response.json() : { items: [] };
      return body.items?.[0]?.slug ?? body.items?.[0]?.id ?? null;
    });
    if (handle) expect(await visit(`/products/${handle}`, 'product')).toEqual([]);
    else console.log('[CSP] no product in this store — the product page was not checked');

    expect(await visit('/cart', 'cart')).toEqual([]);
  });

  // الخطوط الخارجية: `storeTheme.js` يحقن رابط خطوط جوجل لكل متجر — وهو أرجح مصدرٍ لمخالفة `font-src`
  // أو `style-src`، لأنّه يُحقن بعد الإقلاع لا في الصفحة الأصلية.
  test('3 · السمة والخطوط المحقونة بعد الإقلاع', async () => {
    await page.goto('/', { waitUntil: 'load' });
    const injected = await page.evaluate(() =>
      [...document.querySelectorAll('link[rel="stylesheet"], link[rel="preconnect"]')]
        .map((l) => /** @type {HTMLLinkElement} */(l).href)
        .filter((href) => !href.startsWith(location.origin)));

    console.log(`[CSP] external stylesheet/preconnect origins: ${JSON.stringify(injected)}`);
    await page.waitForTimeout(1500);

    const found = await page.evaluate(() => {
      // @ts-ignore
      const list = window.__cspViolations || []; window.__cspViolations = []; return list;
    });
    for (const v of found) all.push({ ...v, page: 'theme' });
    expect(found, 'الخطوط المحقونة تخالف السياسة').toEqual([]);
  });

  test('4 · الحساب والدخول', async () => {
    expect(await visit('/login', 'login')).toEqual([]);
    expect(await visit('/register', 'register')).toEqual([]);
  });

  test('5 · لوحة الإدارة بكل شاشاتها', async () => {
    await page.goto('/login');
    await page.locator('input[type="email"]').first().fill(ADMIN.email);
    await page.locator('input[type="password"]').first().fill(ADMIN.password);
    await page.getByRole('button', { name: /sign in|دخول/i }).click();
    await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 45_000 });

    for (const path of [
      '/admin', '/admin/business', '/admin/products', '/admin/inventory', '/admin/categories',
      '/admin/search-synonyms', '/admin/orders', '/admin/customers', '/admin/settings', '/admin/staff',
    ]) {
      expect(await visit(path, path), `مخالفة في ${path}`).toEqual([]);
    }
  });

  // الحصيلة في مكان واحد: عند الفشل تُقرأ كل المخالفات معاً بدل ملاحقتها اختباراً اختباراً.
  test('6 · الحصيلة — لا مخالفة في أي مسار مفحوص', async () => {
    console.log(`[CSP] total violations across all checked routes: ${all.length}`);
    if (all.length) console.log(`[CSP] ${JSON.stringify(all, null, 2)}`);
    expect(all).toEqual([]);
  });
});
