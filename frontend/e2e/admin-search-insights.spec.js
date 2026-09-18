import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// شاشة أثر البحث على مكدّس حقيقي (M13) — الحلقة كاملةً في متصفّح:
//   زبونٌ يبحث عن كلمة لا يجدها  ⇒  التاجر يراها في شاشته  ⇒  يضيفها مرادفاً بنقرة  ⇒  البحث يجدها.
//
// **ولمَ في متصفّح وقد غُطّي كلّ طرفٍ منها باختبار تكامل؟** لأنّ ما بينها هو المنتج: أنّ الكتابة غير
// المتزامنة تظهر على الشاشة فعلاً، وأنّ زرّ "أضِفها مرادفاً" يحمل الكلمة ولغتها إلى الدرج (وهو تسليمٌ بين
// لسانين ومكوّنين، لا استدعاء دالّة)، وأنّ ما يُحفظ يغيّر نتيجة بحثٍ حقيقيّ بعده. لا اختبار خادمٍ يرى ذلك.
//
// ويُفحص مع ذلك ما لا تراه رحلةٌ سعيدة: الألسنة بلوحة المفاتيح، والوضع الداكن، والعربية والإنجليزية،
// والهاتف — لأنّ هذه شاشةٌ جديدة كاملةً، ومكوّن الألسنة فيها جديدٌ يستعمله غيرُها غداً.
//
// يُشغَّل على حزمة الحاويات:
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/admin-search-insights.spec.js --project=desktop
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};
const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');
const stamp = Date.now().toString(36);

// كلمةٌ لا تشبه شيئاً في أي كتالوج: لا تُطابق، ولا يبلغها التصحيح المطبعي (M3) فيُفسد قياس "لم تجد شيئاً".
const MISSED = `زقمفحص${stamp}`;
// وكلمةٌ تُطابق منتج هذه الرحلة — هي ما سيصير المرادف توسيعاً إليه.
const PRODUCT = `مكنسةفحص${stamp}`;

test.describe.configure({ mode: 'serial' });

const settle = (target) => target.evaluate(() => Promise.all(document.getAnimations().map((a) => a.finished.catch(() => null))));

const axe = async (target) => {
  await settle(target);
  await target.addScriptTag({ content: axeSource });
  return target.evaluate(async () =>
    // @ts-ignore — axe يُحقن في الصفحة
    (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`));
};

test.describe('أثر البحث', () => {
  /** @type {import('@playwright/test').Page} */
  let page;
  let token;
  let productId;
  let synonymId;

  const authed = () => ({ headers: { Authorization: `Bearer ${token}` } });

  const insightsTab = () => page.getByRole('tab').first();
  const vocabularyTab = () => page.getByRole('tab').last();
  const missedRow = () => page.locator('tr', { hasText: MISSED });

  // الكاتب الخلفي يجمع على نافذة ثانيتين: الشاشة تُنعَش حتى يظهر الصفّ، لا انتظارٌ ثابت يكون أطول
  // من اللازم دائماً وأقصر منه أحياناً.
  const waitForRow = async () => {
    await expect.poll(async () => {
      await page.reload();
      await expect(page.getByRole('tablist')).toBeVisible({ timeout: 30_000 });
      return await missedRow().count();
    }, { timeout: 40_000, intervals: [1000, 2000, 2000, 3000] }).toBeGreaterThan(0);
  };

  // بعد أن يُضيف التاجر المرادف تخرج الكلمة من قائمة العمل (صارت تجد نتائج)، فما يحتاجه فحصُ العرض
  // بعدها هو شاشةٌ فيها صفوف لا هذه الكلمة بعينها: يُطفأ المرشّح ويُنتظر أيّ صفّ.
  const showAllAndWaitForRows = async () => {
    await expect(page.getByRole('tablist')).toBeVisible({ timeout: 30_000 });
    const filter = page.getByRole('checkbox');
    if (await filter.isChecked()) await filter.uncheck();
    await expect(page.locator('tbody tr').first()).toBeVisible({ timeout: 30_000 });
  };

  test.beforeAll(async ({ browser, request }) => {
    page = await (await browser.newContext()).newPage();

    const login = await request.post('/api/auth/login', { data: ADMIN });
    expect(login.ok(), 'تعذّر دخول المدير — اضبط SOUQ_E2E_ADMIN_EMAIL/PASSWORD').toBeTruthy();
    token = (await login.json()).accessToken ?? (await login.json()).token;

    const categories = await request.get('/api/admin/categories', authed());
    const created = await request.post('/api/products', {
      ...authed(),
      data: {
        categoryId: (await categories.json())[0].id,
        slug: `qa-si-${stamp}`, price: 30, stockQuantity: 10,
        translations: { ar: { name: PRODUCT }, en: { name: `QA Search ${stamp}` } },
      },
    });
    expect(created.status(), await created.text()).toBe(201);
    productId = (await created.json()).id;

    // زبونٌ (بلا توكن — نقطة عامّة) يبحث مرّتين عن كلمة لا توجد، ومرّةً عن كلمة توجد.
    await request.get(`/api/products?keyword=${encodeURIComponent(MISSED)}`);
    await request.get(`/api/products?keyword=${encodeURIComponent(MISSED)}`);
    await request.get(`/api/products?keyword=${encodeURIComponent(PRODUCT)}`);

    await page.goto('/login');
    await page.locator('input[type="email"]').first().fill(ADMIN.email);
    await page.locator('input[type="password"]').first().fill(ADMIN.password);
    await page.getByRole('button', { name: /sign in|دخول/i }).click();
    await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 45_000 });
  });

  test.afterAll(async () => {
    if (synonymId) await page.request.delete(`/api/admin/search-synonyms/${synonymId}`, authed()).catch(() => {});
    if (productId) await page.request.delete(`/api/products/${productId}`, authed()).catch(() => {});
    await page.context().close();
  });

  test('1 · بحثُ الزبون يصل شاشة التاجر بعدده وحالته', async () => {
    await page.goto('/admin/search-synonyms');
    await waitForRow();

    // اللسان الأول هو الأثر: من يفتح هذه الشاشة يفتحها ليعرف ما يفعل.
    await expect(insightsTab()).toHaveAttribute('aria-selected', 'true');

    const row = missedRow();
    await expect(row).toContainText('2');                      // بُحث عنها مرّتين
    await expect(row).toContainText(/لم تجد شيئاً|Found nothing/);

    // والملخّص يقرأ النافذة لا الصفحة: بحوثٌ ومفردات، ونسبةٌ لا شرطة (فقد جرى بحثٌ فعلاً).
    await expect(page.getByText(/من البحوث لم تجد شيئاً|of searches found nothing/)).toBeVisible();
    await expect(page.locator('body')).not.toContainText('NaN');

    expect(await axe(page)).toEqual([]);
  });

  // ============================================================================
  // الحلقة نفسها — وهي سبب وجود المرحلة: من "هذه الكلمة تفشل" إلى "علّمتُ المحرّك إيّاها" بنقرة،
  // ثم إلى "الزبون صار يجدها" ببحثٍ حقيقيّ بعدها.
  // ============================================================================
  test('2 · "أضِفها مرادفاً" يملأ الدرج، والحفظ يجعل البحث يجدها', async () => {
    await page.goto('/admin/search-synonyms');
    await waitForRow();

    await missedRow().getByRole('button', { name: /أضِفها مرادفاً|Add as synonym/ }).click();

    // ينتقل إلى لسان المفردات كي يرى التاجر نتيجته في سياقها، والدرج مملوءٌ بالكلمة ولغتها.
    await expect(vocabularyTab()).toHaveAttribute('aria-selected', 'true');
    const drawer = page.getByRole('dialog');
    await expect(drawer).toBeVisible();
    await expect(drawer.locator('input').first()).toHaveValue(MISSED);
    await expect(drawer.locator('select')).toHaveValue('ar');

    expect(await axe(page)).toEqual([]);

    // التوسيع وحده على التاجر — وهو القرار الذي لا تملكه الشاشة.
    await drawer.locator('input').nth(1).fill(PRODUCT);
    await drawer.getByRole('button', { name: /save|حفظ/i }).click();
    await expect(drawer).toBeHidden({ timeout: 20_000 });

    await expect(page.locator('tr', { hasText: MISSED }).first()).toContainText(PRODUCT);
    synonymId = await page.evaluate(async (term) => {
      const response = await fetch('/api/admin/search-synonyms', { headers: { Authorization: `Bearer ${localStorage.getItem('souq_access_token') ?? ''}` } });
      const list = response.ok ? await response.json() : [];
      return list.find((s) => s.term === term)?.id ?? null;
    }, MISSED).catch(() => null);

    // الإثبات الأخير: نفس بحث الزبون، وقد صار يجد.
    const found = await page.request.get(`/api/products?keyword=${encodeURIComponent(MISSED)}`);
    expect((await found.json()).totalCount, 'المرادف لم يُغيّر نتيجة بحثٍ حقيقيّ').toBeGreaterThan(0);

    // ============================================================================
    // والحلقة تُغلق على نفسها: الكلمة تخرج من قائمة العمل لأنّها لم تعد تفشل دائماً — لا لأنّ أحداً
    // شطبها. وهذا ما يجعل القائمة قائمة عملٍ حيّة: ما يُصلَح يختفي منها من تلقائه.
    // (تجربتُنا لهذا السلوك هي ما كشف أنّ الافتراض المكتوب في هذا الملفّ أوّلاً كان خاطئاً.)
    // ============================================================================
    await expect.poll(async () => {
      await page.reload();
      await expect(page.getByRole('tablist')).toBeVisible({ timeout: 30_000 });
      return await missedRow().count();
    }, { timeout: 40_000, intervals: [1000, 2000, 2000, 3000] })
      .toBe(0);
  });

  test('3 · مرشّح "لم تجد شيئاً" يُخفي ويُظهر، والفترة تُضيّق', async () => {
    await page.goto('/admin/search-synonyms');
    await expect(page.getByRole('tablist')).toBeVisible({ timeout: 30_000 });

    // كلمةٌ تجد نتائج ليست في القائمة الافتراضية (المرشّح مفعّل) — ومنها MISSED بعد أن أُصلحت.
    await expect(page.locator('tr', { hasText: PRODUCT })).toHaveCount(0);

    await page.getByRole('checkbox').uncheck();
    const row = page.locator('tr', { hasText: PRODUCT }).first();
    await expect(row).toBeVisible({ timeout: 20_000 });
    // والحالة الثالثة تُعرض بنغمتها: كلمةٌ لم تفشل ولا مرّة ليست "فشلت صفر مرّة".
    await expect(row).toContainText(/تجد نتائج دائماً|Always finds results/);

    // والفترة تعمل: كل بحوث هذه الرحلة جرت الآن، فهي داخل أضيق نافذة.
    await page.locator('select').first().selectOption('7');
    await expect(page.locator('tr', { hasText: PRODUCT }).first()).toBeVisible({ timeout: 20_000 });
  });

  // ============================================================================
  // الألسنة بلوحة المفاتيح — الجزء الذي لا يراه أحد ولا يعمل إن لم يُكتب: Tab يدخل المجموعة **مرّة**،
  // والأسهم تنقل داخلها. والواجهة عربية هنا، فالسهم الأيسر يمضي إلى الأمام.
  // ============================================================================
  test('4 · الألسنة تُدار بلوحة المفاتيح في اتجاه القراءة', async () => {
    await page.goto('/admin/search-synonyms');
    await expect(page.getByRole('tablist')).toBeVisible({ timeout: 30_000 });

    const rtl = await page.evaluate(() => document.documentElement.dir === 'rtl');
    await insightsTab().focus();
    await page.keyboard.press(rtl ? 'ArrowLeft' : 'ArrowRight');

    await expect(vocabularyTab()).toHaveAttribute('aria-selected', 'true');
    await expect(vocabularyTab()).toBeFocused();
    // لسانٌ واحد في تسلسل الجدولة: غير المختار خارجه.
    await expect(insightsTab()).toHaveAttribute('tabindex', '-1');

    await page.keyboard.press(rtl ? 'ArrowRight' : 'ArrowLeft');
    await expect(insightsTab()).toHaveAttribute('aria-selected', 'true');
  });

  // الوضع الداكن: الشارات والألسنة والبطاقات بلا رقم لونٍ حرفيّ — وهو ما كان يكسر ثلاث نغمات قبل M10.
  test('5 · الوضع الداكن يبقى مقروءاً', async () => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto('/admin/search-synonyms');
    await showAllAndWaitForRows();

    expect(await axe(page)).toEqual([]);
    await page.emulateMedia({ colorScheme: 'light' });
  });

  // الإنجليزية: الشاشة كاملةً مترجمة، ولا مفتاح خام يظهر (مثل admin.searchInsights.colTerm).
  test('6 · الإنجليزية بلا مفاتيح خام ولا تجاوز أفقي', async () => {
    await page.goto('/admin/search-synonyms');
    await expect(page.getByRole('tablist')).toBeVisible({ timeout: 30_000 });

    await page.evaluate(() => localStorage.setItem('i18nextLng', 'en'));
    await page.reload();
    await showAllAndWaitForRows();

    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.locator('body')).not.toContainText(/admin\.(searchInsights|storeSearch)\./);

    const overflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, 'الصفحة تمرّر أفقياً').toBeLessThanOrEqual(1);

    expect(await axe(page)).toEqual([]);

    await page.evaluate(() => localStorage.setItem('i18nextLng', 'ar'));
  });
});
