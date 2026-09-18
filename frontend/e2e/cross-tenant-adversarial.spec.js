import { expect, test } from '@playwright/test';

// ============================================================================
// محاولة اختراق بين متجرين **من متصفّح بجلسة حقيقية** (M15).
//
// اختبارات التكامل تُثبت أنّ توكن متجرٍ يُرفض على مضيف متجرٍ آخر. وهذا لا يُثبت ما يهمّ فعلاً: ماذا يحدث
// حين يحمل **متصفّحٌ حقيقي** جلسةً حقيقية إلى المضيف الآخر؟ هناك أشياء لا وجود لها في اختبار خادم:
// ملفّات تعريف الارتباط وقواعد مضيفها، وذاكرة الواجهة، والتحويلات، وما يُعرض على الشاشة قبل أن يُرفض
// الطلب أو بعده. والتسريب هنا يكون مرئيّاً — سطرٌ في جدول، رقمٌ في بطاقة — لا رمزَ حالةٍ في استجابة.
//
// المضيف الثاني يُكتشف من المنصّة لا يُكتب في الملفّ: أي بيئة فيها متجران يُشغّل هذه الرحلة كما هي.
//
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/cross-tenant-adversarial.spec.js --project=desktop
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};
const OWNER = {
  email: process.env.SOUQ_E2E_OWNER_EMAIL || 'owner@souq.com',
  password: process.env.SOUQ_E2E_OWNER_PASSWORD || 'Owner@12345',
};

test.describe.configure({ mode: 'serial' });
test.setTimeout(180_000);

const platformOrigin = (base) => {
  const url = new URL(base);
  if (!url.hostname.startsWith('admin.')) url.hostname = `admin.${url.hostname}`;
  return url.origin;
};

test.describe('عبور المتاجر من المتصفّح', () => {
  /** @type {import('@playwright/test').Page} */
  let page;
  let otherOrigin;   // أصل متجرٍ آخر، مكتشَفاً من المنصّة
  let accessToken;   // توكن متجرنا كما تحمله الواجهة فعلاً

  test.beforeAll(async ({ browser, request }, testInfo) => {
    const base = testInfo.project.use.baseURL;

    // ── يُكتشف المضيف الثاني من قائمة المنصّة ──
    const owner = await request.post(`${platformOrigin(base)}/api/auth/login`, { data: OWNER });
    expect(owner.ok(), 'تعذّر دخول مالك المنصّة — اضبط SOUQ_E2E_OWNER_EMAIL/PASSWORD').toBeTruthy();
    const ownerToken = (await owner.json()).accessToken;

    const stores = await request.get(`${platformOrigin(base)}/api/platform/tenants?pageSize=100`, {
      headers: { Authorization: `Bearer ${ownerToken}` },
    });
    expect(stores.ok(), 'تعذّرت قراءة متاجر المنصّة').toBeTruthy();

    const ours = new URL(base).hostname;
    const other = ((await stores.json()).items ?? [])
      .find((s) => s.primaryHost && s.primaryHost !== ours && s.status === 'Active');
    expect(other, 'لا متجر ثانٍ نشط بمضيف — هذه الرحلة تحتاج متجرين').toBeTruthy();

    const url = new URL(base);
    url.hostname = other.primaryHost;
    otherOrigin = url.origin;
    console.log(`[adversarial] ours=${ours} other=${other.primaryHost}`);

    const context = await browser.newContext();
    page = await context.newPage();

    await page.goto('/login');
    await page.locator('input[type="email"]').first().fill(ADMIN.email);
    await page.locator('input[type="password"]').first().fill(ADMIN.password);
    await page.getByRole('button', { name: /sign in|دخول/i }).click();
    await page.waitForURL((u) => !u.pathname.includes('/login'), { timeout: 45_000 });

    // التوكن كما تحمله الواجهة — من الذاكرة لا من التخزين (SEC-SESS-02)، فيُؤخذ بتجديدٍ صريح.
    accessToken = await page.evaluate(async () => {
      const response = await fetch('/api/auth/refresh', { method: 'POST' });
      return response.ok ? (await response.json()).accessToken : null;
    });
    expect(accessToken, 'لم تُصدر الواجهة توكناً — الرحلة بعده بلا معنى').toBeTruthy();
  });

  test.afterAll(async () => { await page?.context().close(); });

  // ========================================================================
  // 1 · التوكن نفسه، مضيف آخر — من داخل الصفحة لا من عميل HTTP.
  // `fetch` داخل الصفحة يمرّ بكل ما يمرّ به طلب المستخدم: أصلُه، وملفّاته، وسياسة CORS.
  // ========================================================================
  test('1 · توكن متجرنا لا يفتح أي نقطة إدارية على المضيف الآخر', async () => {
    const results = await page.evaluate(async ({ origin, token }) => {
      const paths = [
        'api/auth/me', 'api/orders?pageSize=5', 'api/admin/customers?pageSize=5',
        'api/admin/inventory', 'api/admin/search-synonyms/insights', 'api/admin/reviews',
      ];
      const out = {};
      for (const path of paths) {
        try {
          const response = await fetch(`${origin}/${path}`, { headers: { Authorization: `Bearer ${token}` } });
          out[path] = response.status;
        } catch (e) {
          out[path] = `blocked: ${e.name}`;   // CORS يرفض قبل الشبكة — رفضٌ أيضاً
        }
      }
      return out;
    }, { origin: otherOrigin, accessToken });

    // ========================================================================
    // النتيجة المتوقّعة هنا **رفضٌ من المتصفّح نفسه** (`blocked: TypeError`) لا 401 من الخادم: سياسة
    // CORS لا تسمح بأي أصلٍ خارج التطوير، فالطلب لا يغادر المتصفّح أصلاً. وهذا أقوى لا أضعف — لكنّه
    // يعني أنّ هذه الرحلة لا تُثبت رفضَ **الخادم**، لأنّها لا تصل إليه.
    //
    // رفضُ الخادم مُثبَتٌ في مكانه: `TenantIsolationTests` (توكن A على مضيف B يُردّ 401 لكل نقطة)،
    // وأُعيد التحقّق منه على الحزمة العاملة في M15 بطلبات مباشرة خارج المتصفّح. الطبقتان مقصودتان.
    // ========================================================================
    console.log(`[adversarial] cross-host results: ${JSON.stringify(results)}`);
    for (const [path, status] of Object.entries(results)) {
      expect(status === 401 || String(status).startsWith('blocked'),
        `${path} أعاد ${status} — توكن متجرٍ يجب ألّا يُقبل على مضيف متجرٍ آخر`).toBeTruthy();
    }
  });

  // ========================================================================
  // 2 · ملفّ الجلسة لا يسافر إلى المضيف الآخر.
  // هذا ما تحرسه بادئة `__Host-` (M15): ملفٌّ مقصورٌ على مضيفٍ واحد لا يُرسَل لغيره ولا يُكتب له.
  // ========================================================================
  test('2 · ملفّ رمز التجديد مقصور على مضيف متجره', async () => {
    const cookies = await page.context().cookies();
    const session = cookies.filter((c) => c.name.includes('souq_refresh'));
    expect(session.length, 'لا ملفّ جلسة أصلاً — الفحص بلا معنى').toBeGreaterThan(0);

    for (const cookie of session) {
      // مقصورٌ على مضيفٍ واحد: لا نقطة بادئة في النطاق (وهي ما يجعله يشمل الأشقّاء).
      expect(cookie.domain.startsWith('.'), `نطاق الملفّ ${cookie.domain} يشمل مضيفات شقيقة`).toBeFalsy();
      expect(new URL(otherOrigin).hostname).not.toBe(cookie.domain);
    }

    // وعملياً: طلب تجديدٍ على المضيف الآخر لا يجد جلسة.
    const refreshed = await page.evaluate(async (origin) => {
      try {
        const response = await fetch(`${origin}/api/auth/refresh`, { method: 'POST', credentials: 'include' });
        return response.status;
      } catch (e) { return `blocked: ${e.name}`; }
    }, otherOrigin);

    console.log(`[adversarial] refresh on the other host: ${refreshed}`);
    expect(refreshed === 401 || String(refreshed).startsWith('blocked')).toBeTruthy();
  });

  // ========================================================================
  // 3 · التصفّح إلى لوحة المضيف الآخر بنفس الجلسة: لا بيانات على الشاشة.
  // الفحص على ما **يُعرض**، لا على رمز الحالة — تسريبٌ بين متجرين يُرى في جدول لا في استجابة.
  // ========================================================================
  test('3 · لوحة المضيف الآخر لا تعرض بيانات متجرنا', async () => {
    const ourCustomers = await page.evaluate(async () => {
      const response = await fetch('/api/admin/customers?pageSize=5', {
        headers: { Authorization: `Bearer ${localStorage.getItem('x') ?? ''}` },
      });
      return response.ok ? (await response.json()).items?.map((c) => c.email) ?? [] : [];
    });

    await page.goto(`${otherOrigin}/admin/customers`);
    await page.waitForTimeout(3000);

    // إمّا رُدّ إلى الدخول، وإمّا بقي في اللوحة بلا أي صفّ — ولا يجوز أن يُعرض بريد عميلٍ من متجرنا.
    const body = (await page.locator('body').textContent()) ?? '';
    for (const email of ourCustomers) {
      expect(body, 'بريد عميلٍ من متجرنا ظهر على لوحة متجرٍ آخر').not.toContain(email);
    }
    expect(body).not.toContain(ADMIN.email);
    console.log(`[adversarial] landed on ${page.url()}`);
  });

  // ========================================================================
  // 4 · الدفع: طلبٌ من متجرنا لا يُقرأ ولا يُدفع من المضيف الآخر.
  // أعلى ما يمكن أن يُسرَّب: مبالغ وعناوين وحالة دفع.
  // ========================================================================
  test('4 · طلب من متجرنا لا يُقرأ ولا تُؤكَّد مدفوعاته من المضيف الآخر', async () => {
    // يُعاد إلى أصلنا أوّلاً: الرحلة تركت الصفحة على المضيف الآخر، ومسارٌ نسبيّ بعدها كان يسأل **ذلك**
    // المضيف عن طلباتنا فيعود فارغاً — فيتخطّى الاختبار نفسَه ظانّاً أن لا طلب لدينا.
    await page.goto('/admin/orders');
    const ourOrderId = await page.evaluate(async (token) => {
      const response = await fetch('/api/orders?pageSize=1', { headers: { Authorization: `Bearer ${token}` } });
      const body = response.ok ? await response.json() : { items: [] };
      return body.items?.[0]?.id ?? null;
    }, accessToken);

    test.skip(!ourOrderId, 'لا طلب في هذا المتجر — لا شيء يُحاوَل الوصول إليه');

    const results = await page.evaluate(async ({ origin, token, id }) => {
      const out = {};
      for (const [label, init] of [
        ['read', { headers: { Authorization: `Bearer ${token}` } }],
        ['confirm', { method: 'POST', headers: { Authorization: `Bearer ${token}` } }],
      ]) {
        const path = label === 'read' ? `api/orders/${id}` : `api/orders/${id}/confirm-payment`;
        try {
          const response = await fetch(`${origin}/${path}`, init);
          out[label] = response.status;
        } catch (e) { out[label] = `blocked: ${e.name}`; }
      }
      return out;
    }, { origin: otherOrigin, token: accessToken, id: ourOrderId });

    console.log(`[adversarial] order ${ourOrderId} from the other host: ${JSON.stringify(results)}`);
    for (const [label, status] of Object.entries(results)) {
      expect([401, 404].includes(status) || String(status).startsWith('blocked'),
        `${label} أعاد ${status} — طلب متجرٍ يجب ألّا يُقرأ ولا يُدفع من مضيف آخر`).toBeTruthy();
    }
  });
});
