import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// شاشة الجرد على مكدّس حقيقي (M7) — الشاشة التي يقرأ منها التاجر رقماً ثم يتصرّف بناءً عليه.
//
// ما لم يكن مُغطّى في متصفّح قبل هذا الملفّ: سجلّ حركة متغيّر **من شاشة الجرد** (كان مُغطّى من صفحة
// المتغيّرات وحدها، وهما يتشاركان الدرج نفسه لكن لا اختبار يفتحه من هنا)، والتصحيح من هذه الشاشة،
// وأنّ التصحيح يُنعش **الجدول وشريط التنبيه معاً**.
//
// ذلك الأخير هو ما تغيّر في M7: الشاشة انتقلت إلى طبقة الاستعلام (جزء من TD-25)، والإنعاش صار إبطالاً
// لجذر `['inventory']` الذي يحمل الصفحة وعدد المنخفض. لو أبطل أحدهما دون الآخر لقرأ التاجر جدولاً جديداً
// فوقه عدد قديم — وهو نوع الخطأ الذي لا يظهر في اختبار مكوّن ولا في لقطة، بل في رقم يثق به ولم يعد صحيحاً.
// السباق على الترقيم نفسه مُثبَّت في src/pages/admin/Inventory.test.jsx (يحتاج ≥50 صفّاً لا تُبذَر هنا).
//
// يُشغَّل على حزمة الحاويات:
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/admin-inventory.spec.js --project=desktop
//
// أمّا الهاتف فليس هنا: مشروع `phone` وحده يُشغّل محاكاة جهاز حقيقية (Pixel 7)، وهي ما يجعل
// `pointer: coarse` يسري. عرضُ نافذةٍ ضيّقٍ في متصفّح سطح مكتب لا يجعل المؤشّر خشناً، فاختبارُ هدف
// لمسٍ هنا كان سيقيس القاعدة الخطأ. شاشة الجرد على هاتف — التمرير والأهداف — في responsive.spec.js.
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};
const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');
const stamp = Date.now().toString(36);

test.describe.configure({ mode: 'serial' });

// حركةٌ لا تنتهي لا يُنتظَر انتهاؤها — وإلّا عُلِّق الانتظار إلى أن تنفد المهلة. شريط الإعلان
// يمرّ إلى الأبد على كل صفحة متجر، والهيكل يلمع، والدوّار يدور: ثلاثتها `iterations: Infinity`.
const settle = (target) => target.evaluate(() => Promise.all(document.getAnimations()
    .filter((a) => a.effect?.getTiming?.().iterations !== Infinity)
    .map((a) => a.finished.catch(() => null))));

// الصفحة تمرّر بسلاسة، وقائمة إجراءات الصفّ تُغلق عند أيّ تمرير (موضعها fixed): فيُكمَل التمرير أوّلاً ثم
// يُنقَر — كما يفعل المستخدم، لا نقراً في منتصف حركةٍ تُغلق القائمة التي فتحها. هذا TD-43 بعينه وعلاجُه
// المكتوب فيه (يستعمله product-variants.spec.js)، ويُستعمل هنا للسبب نفسه: على قاعدةٍ فيها صفوفٌ كثيرة
// يفشل النقر بـ "element was detached from the DOM" بلا أيّ عيب في المنتج (M10).

const axe = async (target) => {
  await settle(target);
  await target.addScriptTag({ content: axeSource });
  return target.evaluate(async () =>
    // @ts-ignore — axe يُحقن في الصفحة
    (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`));
};

test.describe('شاشة الجرد', () => {
  /** @type {import('@playwright/test').Page} */
  let page;
  let token;
  let productId;
  let variantId;
  const storeName = `QA مكنسة ${stamp}`;

  const authed = () => ({ headers: { Authorization: `Bearer ${token}` } });

  const row = () => page.locator('tr', { hasText: storeName });
  // الأعمدة بترتيبها في Inventory.jsx: صورة، اسم، فئة، موجود، محجوز، متاح، حدّ التنبيه، إجراءات.
  // تُقرأ خليّةً خليّة لا كنصّ صفٍّ واحد: النصّ المدموج يجعل "2" و"0" و"20" و"5" تقرأ "2025".
  const cell = (index) => row().locator('td').nth(index);
  const [ON_HAND, RESERVED, AVAILABLE, THRESHOLD] = [3, 4, 5, 6];

  const openRowMenu = async () => {
    const trigger = row().getByRole('button').last();
    await trigger.scrollIntoViewIfNeeded();
    await expect.poll(() => page.evaluate(() => new Promise((resolve) => {
      const before = window.scrollY;
      requestAnimationFrame(() => requestAnimationFrame(() => resolve(window.scrollY === before)));
    }))).toBe(true);
    await trigger.click();
    await expect(page.getByRole('menu')).toBeVisible();
  };

  // دخول واحد للملفّ كلّه: حدّ الدخول 10 في الدقيقة (DeveloperQualityGates).
  test.beforeAll(async ({ browser, request }) => {
    page = await (await browser.newContext()).newPage();

    const login = await request.post('/api/auth/login', { data: ADMIN });
    expect(login.ok(), 'تعذّر دخول المدير — اضبط SOUQ_E2E_ADMIN_EMAIL/PASSWORD').toBeTruthy();
    token = (await login.json()).accessToken ?? (await login.json()).token;

    // منتج لهذه الرحلة وحدها، بمخزون فوق حدّ التنبيه كي يكون العبور **أثرَ التصحيح** لا حالةً مسبقة.
    const categories = await request.get('/api/admin/categories', authed());
    const created = await request.post('/api/products', {
      ...authed(),
      data: {
        categoryId: (await categories.json())[0].id,
        slug: `qa-inv-${stamp}`, price: 25, stockQuantity: 20,
        translations: { ar: { name: storeName }, en: { name: `QA Vacuum ${stamp}` } },
      },
    });
    expect(created.status(), await created.text()).toBe(201);
    productId = (await created.json()).id;

    const inventory = await request.get('/api/admin/inventory?pageSize=100', authed());
    variantId = (await inventory.json()).items.find((i) => i.id === productId).variantId;

    await page.goto('/login');
    await page.locator('input[type="email"]').first().fill(ADMIN.email);
    await page.locator('input[type="password"]').first().fill(ADMIN.password);
    await page.getByRole('button', { name: /sign in|دخول/i }).click();
    await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 45_000 });
  });

  test.afterAll(async () => {
    if (productId) await page.request.delete(`/api/products/${productId}`, authed()).catch(() => {});
    await page.context().close();
  });

  test('1 · الصفّ يُقرأ كاملاً: الموجود والمحجوز والمتاح وحدّ التنبيه', async () => {
    await page.goto('/admin/inventory');
    await expect(row()).toBeVisible({ timeout: 45_000 });

    // الأرقام تُقرأ نصّاً لا لوناً (ADR-0036): 20 موجوداً، صفر محجوزاً، 20 متاحاً، وحدّ التنبيه 5.
    await expect(cell(ON_HAND)).toHaveText('20');
    await expect(cell(RESERVED)).toHaveText('0');
    await expect(cell(AVAILABLE)).toHaveText('20');
    await expect(cell(THRESHOLD)).toHaveText('5');
    expect(await axe(page)).toEqual([]);
  });

  test('2 · سجلّ الحركة يُفتح من شاشة الجرد ويُظهر الرصيد الابتدائي', async () => {
    await page.goto('/admin/inventory');
    await expect(row()).toBeVisible({ timeout: 45_000 });
    await openRowMenu();
    await page.getByRole('menuitem', { name: /stock history|سجلّ الحركة|History/i }).click();

    const drawer = page.getByRole('dialog');
    // فتح المنتج يكتب حركة شراء ابتدائية بمقدار المخزون الافتتاحي — أوّل ما يجب أن يراه المدقّق.
    await expect(drawer).toContainText(/20/);
    expect(await axe(page)).toEqual([]);
    await page.keyboard.press('Escape');
    await expect(drawer).toBeHidden();
  });

  test('3 · تصحيح من هذه الشاشة يُنعش الجدول وشريط التنبيه معاً', async () => {
    await page.goto('/admin/inventory');
    await expect(row()).toBeVisible({ timeout: 45_000 });

    // عدد المنخفض قبل التصحيح — الشريط قد يكون غائباً إن لم يكن في المتجر منخفض بعد.
    const banner = page.locator('[class*="lowStockAlert"]');
    const before = (await banner.count()) ? await banner.innerText() : '';

    await openRowMenu();
    await page.getByRole('menuitem', { name: /adjust|تصحيح/i }).click();
    const drawer = page.getByRole('dialog');
    // −18 من 20 ⇒ 2، وحدّ التنبيه الافتراضي 5 ⇒ عبورٌ نازل: الصفّ يصير منخفضاً والعدد يزيد.
    await drawer.locator('input[type="number"]').first().fill('-18');
    await drawer.getByRole('textbox').first().fill(`QA جرد ${stamp}`);
    await drawer.getByRole('button', { name: /^Save$|^حفظ$/ }).click();
    await expect(drawer).toBeHidden({ timeout: 20_000 });

    // الجدول أُنعش: الموجود والمتاح صارا 2 (والمحجوز صفر كما كان).
    await expect(cell(AVAILABLE)).toHaveText('2', { timeout: 20_000 });
    await expect(cell(ON_HAND)).toHaveText('2');
    // والشريط أُنعش معه: هذا هو ما يكسره إبطالُ مفتاحٍ واحد من الاثنين.
    await expect(banner).toBeVisible({ timeout: 20_000 });
    await expect(banner).not.toHaveText(before);

    // والسبب وصل إلى الدفتر — لا رقم بلا سببه.
    const ledger = await page.request.get(`/api/admin/inventory/variants/${variantId}/movements?pageSize=10`, authed());
    const movements = (await ledger.json()).items;
    expect(movements[0]).toMatchObject({ type: 'Adjustment', quantityChange: -18, newQuantity: 2 });
    expect(movements[0].note).toContain(`QA جرد ${stamp}`);
  });

});
