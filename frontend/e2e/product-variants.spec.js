import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// خيارات المنتج ومتغيّراته على مكدّس حقيقي (ProductVariants.md V2، ADR-0040) — كما يعمل التاجر:
//
//   1. منتج بسيط يصير "المقاس" (المتغيّر القائم يأخذ M)، ثم "اللون" (يأخذ Red)، والحفظ يبقى بعد إعادة التحميل.
//   2. إنشاء تركيبتين ناقصتين غير نشطتين، تفعيل إحداهما (يُخفي المنتج من الواجهة، بتأكيد)، تعديل سعرها وSKU، جعلها
//      افتراضية وتعطيل القديمة (يعود المنتج للواجهة بسعرها) — والافتراضي لا يُعطَّل.
//   3. القواعد: قيمة مستخدمة بلا حذف، اسم ناقص يُفحص قبل الإرسال، وحذف خيار يُكرّر تركيبتين يرفضه الخادم برسالة مفهومة.
//   4. المخزون: تصحيح مخزون متغيّر من صفحته وسجلّ حركته، وصفّه في الجرد بوصفه.
//   5. بالعربية (RTL) وفي الوضع الداكن بلا مخالفة إتاحة، ثم على عرض هاتف بلا تمرير أفقي.
//
// الفئة والمنتج يُنشآن لهذه الرحلة ببادئة QA ويُؤرشف المنتج في آخرها. دخول واحد (حدّ الدخول 10 في الدقيقة).
// ============================================================================
// بيانات المدير من البيئة بقيمها الافتراضية (M10): كانت مكتوبةً حرفيّاً، فلم يكن هذا الملفّ قابلاً
// للتشغيل إلا على حزمةٍ مبذورةٍ بهذين تحديداً — وعلى غيرها يفشل عند الدخول بمهلةٍ منتهية، لا برسالةٍ
// تقول السبب. (العلّة نفسها التي كانت في مضيف المنصّة داخل responsive.spec.js، صُحِّحت في M9.)
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};
const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');
const stamp = Date.now().toString(36);
const shots = process.env.QA_SHOTS ?? 'test-results';

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

test.describe('خيارات المنتج ومتغيّراته', () => {
  /** @type {import('@playwright/test').Page} */
  let page;
  let token;
  let productId;
  const productName = `QA Variants ${stamp}`;
  // لغة المتجر الافتراضية تسمّي المنتج في قوائم الإدارة والجرد (لقطة الاسم بلغة المتجر).
  const storeName = `QA قميص ${stamp}`;
  const sku = `QAV${stamp}`.toUpperCase();

  const authed = (extra = {}) => ({ headers: { Authorization: `Bearer ${token}` }, ...extra });

  test.beforeAll(async ({ browser, request }) => {
    page = await browser.newPage({ locale: 'en-US' });
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('qa-lang-set')) {
        localStorage.setItem('souq_lang', 'en');
        localStorage.setItem('souq_theme', 'light');
        sessionStorage.setItem('qa-lang-set', '1');
      }
    });
    await page.goto('/login');
    await page.locator('input[type="email"]').first().fill(ADMIN.email);
    await page.locator('input[type="password"]').first().fill(ADMIN.password);
    await page.getByRole('button', { name: /sign in|دخول/i }).click();
    await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 45_000 });

    // التهيئة عبر HTTP (منتج بسيط كما تُنشئه الإدارة) — الخيارات والمتغيّرات كلها من الشاشة.
    token = (await (await request.post('/api/auth/login', { data: ADMIN })).json()).accessToken;
    const category = await request.post('/api/categories', authed({
      data: { slug: `qa-variants-${stamp}`, translations: { en: { name: `QA Variants ${stamp}` }, ar: { name: `QA متغيّرات ${stamp}` } }, sortOrder: 998, isActive: true },
    }));
    expect(category.status()).toBe(201);
    const created = await request.post('/api/products', authed({
      data: {
        categoryId: (await category.json()).id, price: 20, stockQuantity: 6, sku, status: 'Active',
        translations: { en: { name: productName }, ar: { name: storeName } },
      },
    }));
    expect(created.status()).toBe(201);
    productId = (await created.json()).id;
  });

  test.afterAll(async ({ request }) => {
    if (productId) await request.delete(`/api/products/${productId}`, authed());
    await page?.close();
  });

  const variantRow = (label) => page.locator('table tbody tr').filter({ has: page.getByText(label, { exact: true }) });
  // الصفحة تمرّر بسلاسة (scroll-behavior: smooth)، وقائمة الصفّ تُغلق عند أي تمرير (موضعها fixed): يُكمل التمرير أولاً ثم
  // النقر — كما يفعل المستخدم، لا نقراً في منتصف حركة تُغلق القائمة التي فتحها.
  const openRowMenu = async (label) => {
    const trigger = variantRow(label).getByRole('button');
    await trigger.scrollIntoViewIfNeeded();
    await expect.poll(() => page.evaluate(() => new Promise((resolve) => {
      const before = window.scrollY;
      requestAnimationFrame(() => requestAnimationFrame(() => resolve(window.scrollY === before)));
    }))).toBe(true);
    await trigger.click();
    await expect(page.getByRole('menu')).toBeVisible();
  };
  const storefrontStatus = async (request) => (await request.get(`/api/products/${productId}`)).status();
  const storefrontPrice = async (request) => (await (await request.get(`/api/products/${productId}`)).json()).price;

  test('1 · a simple product becomes Size and Colour, and survives a reload', async ({ request }) => {
    await page.goto('/admin/products');
    await page.getByPlaceholder(/search/i).fill(productName);
    const row = page.locator('tr', { hasText: sku });
    await row.waitFor({ timeout: 45_000 });
    await row.getByRole('button').last().click();
    await page.getByRole('menuitem', { name: 'Options & variants' }).click();
    await page.waitForURL(new RegExp(`/admin/products/${productId}/variants$`));

    await expect(page.getByRole('heading', { name: 'Options and variants', level: 2 })).toBeVisible();
    await expect(page.getByText('No options yet. Add an option')).toBeVisible();
    await expect(variantRow('Single variant')).toContainText(sku);

    // المقاس: S, M, L — والمتغيّر القائم يأخذ M صراحةً.
    await page.getByRole('button', { name: 'Add option' }).click();
    await page.getByLabel('Option name (Arabic)').fill('المقاس');
    await page.getByLabel('Option name (English)').fill('Size');
    await page.getByLabel('Value (Arabic) 1').fill('S');
    await page.getByRole('button', { name: 'Add value' }).click();
    await page.getByLabel('Value (Arabic) 2').fill('M');
    await page.getByRole('button', { name: 'Add value' }).click();
    await page.getByLabel('Value (Arabic) 3').fill('L');
    await page.getByLabel('Existing variants take').selectOption({ label: 'M' });
    await expect(page.getByText('Unsaved changes to the options')).toBeVisible();
    await page.getByRole('button', { name: 'Save options' }).click();
    await expect(page.getByText('Options saved')).toBeVisible();
    await expect(variantRow('M')).toContainText('Default');

    // اللون: أحمر/Red وأزرق/Blue — المتغيّر يأخذ Red.
    await page.getByRole('button', { name: 'Add option' }).click();
    await page.getByLabel('Option name (Arabic)').nth(1).fill('اللون');
    await page.getByLabel('Option name (English)').nth(1).fill('Colour');
    const colour = page.locator('li').filter({ has: page.getByText('Option 2') });
    await colour.getByLabel('Value (Arabic) 1').fill('أحمر');
    await colour.getByLabel('Value (English) 1').fill('Red');
    await colour.getByRole('button', { name: 'Add value' }).click();
    await colour.getByLabel('Value (Arabic) 2').fill('أزرق');
    await colour.getByLabel('Value (English) 2').fill('Blue');
    await colour.getByLabel('Existing variants take').selectOption({ index: 0 });
    await page.getByRole('button', { name: 'Save options' }).click();
    await expect(page.getByText('Options saved')).toBeVisible();

    await page.reload();
    await expect(variantRow('M / Red')).toContainText('Default');
    await expect(page.getByLabel('Option name (English)').nth(1)).toHaveValue('Colour');
    expect(await storefrontStatus(request)).toBe(200);
  });

  test('2 · create combinations, activate, price, move the default and deactivate the old one', async ({ request }) => {
    const create = page.getByRole('region', { name: 'Create variants' });
    await expect(create.getByRole('group', { name: 'Combinations to create' }).getByRole('checkbox')).toHaveCount(5);
    await create.getByLabel('L / Red').check();
    await create.getByLabel('S / Blue').check();
    await create.getByLabel('Price (store currency)').fill('22');
    await create.getByLabel('Opening stock (each)').fill('3');
    // المتغيّر الجديد نشط افتراضياً منذ V3: المنتج يُعرض ويُختار متغيّره، فلا سبب لإنشائه معطّلاً.
    await expect(create.getByLabel('Make the new variants active')).toBeChecked();
    await create.getByRole('button', { name: 'Create 2 variants' }).click();
    await expect(page.getByText('2 variants created')).toBeVisible();
    await expect(variantRow('L / Red')).toContainText('Active');
    await expect(variantRow('S / Blue')).toContainText('3 available');

    // بأربعة متغيّرات نشطة يبقى المنتج معروضاً (أُزيلت بوّابة V2 في V3)، وسعره أرخص ما يمكن شراؤه — لا سعر الافتراضي.
    expect(await storefrontStatus(request)).toBe(200);
    expect(await storefrontPrice(request)).toBe(20);

    await openRowMenu('L / Red');
    await page.getByRole('menuitem', { name: 'Edit price and SKU' }).click();
    const drawer = page.getByRole('dialog');
    await drawer.getByLabel('Price (store currency)').fill('23.5');
    await drawer.getByLabel('SKU').fill(`${sku}-lr`);
    await drawer.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Variant updated')).toBeVisible();
    await expect(variantRow('L / Red')).toContainText(`${sku}-LR`);

    await openRowMenu('L / Red');
    await page.getByRole('menuitem', { name: 'Make default' }).click();
    await expect(page.getByText('Default variant changed')).toBeVisible();
    await expect(variantRow('L / Red')).toContainText('Default');

    // الافتراضي لا يُعطَّل؛ القديم يُعطَّل بتأكيد، فيعود للمنتج متغيّر نشط واحد ويظهر في الواجهة بسعره.
    await openRowMenu('L / Red');
    await expect(page.getByRole('menuitem', { name: 'Deactivate' })).toBeDisabled();
    await page.keyboard.press('Escape');
    await openRowMenu('M / Red');
    await page.getByRole('menuitem', { name: 'Deactivate' }).click();
    await page.getByRole('button', { name: 'Deactivate variant' }).click();
    await expect(page.getByText('Variant deactivated')).toBeVisible();

    // المعطّل يخرج من حساب السعر المعروض: أرخص ما بقي يمكن شراؤه هو 22 (S / Blue).
    const storefront = await request.get(`/api/products/${productId}`);
    expect(storefront.status()).toBe(200);
    expect((await storefront.json()).price).toBe(22);
  });

  test('3 · rules: used values stay, names are checked, and a colliding removal is explained', async () => {
    // الاسم يُعزل اتجاهه بمحرفَي FSI/PDI (مُنسِّق bidi) — فالمطابقة بنمط لا بنصّ حرفي.
    await expect(page.getByRole('button', { name: /^Remove value \u2068?M\u2069?$/ })).toBeDisabled();
    await expect(page.getByText('Used by a variant — deactivate the variant instead of removing the value.').first()).toBeVisible();

    await page.getByRole('button', { name: 'Add option' }).click();
    await page.getByRole('button', { name: 'Save options' }).click();
    await expect(page.getByText('Every option needs a name in the store language (ar).')).toBeVisible();
    await page.getByRole('button', { name: 'Discard changes' }).click();

    // حذف "المقاس": L / Red وM / Red يصبحان Red مرّتين — الخادم يرفض، والرسالة بلغة الواجهة.
    await page.locator('li').filter({ has: page.getByText('Option 1') }).getByRole('button', { name: 'Remove option' }).click();
    await page.getByRole('button', { name: 'Save options' }).click();
    await expect(page.getByText('After this change two variants would have the same values. Adjust the variants first.')).toBeVisible();
    await page.getByRole('button', { name: 'Discard changes' }).click();
    await page.reload();
    await expect(page.getByLabel('Option name (English)').first()).toHaveValue('Size');
  });

  test('4 · stock is corrected per variant and listed per variant in Inventory', async ({ request }) => {
    await openRowMenu('S / Blue');
    await page.getByRole('menuitem', { name: 'Adjust stock' }).click();
    const drawer = page.getByRole('dialog');
    await expect(drawer).toContainText('S / Blue');
    await drawer.getByLabel('Change (positive to add, negative to remove)').fill('-3');
    await drawer.getByLabel('Reason').fill('QA stock count');
    await drawer.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Stock updated')).toBeVisible();
    await expect(variantRow('S / Blue')).toContainText('0 available');

    await openRowMenu('S / Blue');
    await page.getByRole('menuitem', { name: 'Stock history' }).click();
    await expect(page.getByRole('dialog')).toContainText('QA stock count');
    await page.keyboard.press('Escape');

    const inventory = await request.get('/api/admin/inventory?pageSize=100', authed());
    const rows = (await inventory.json()).items.filter((item) => item.id === productId);
    expect(rows.map((item) => item.variantLabel).sort()).toEqual(['L / أحمر', 'M / أحمر', 'S / أزرق']);

    // الأقلّ متاحاً أولاً: S / Blue (صفر) في الصفحة الأولى من الجرد، بوصفه تحت اسم المنتج.
    await page.goto('/admin/inventory');
    const inventoryRow = page.locator('tr', { hasText: storeName }).filter({ hasText: 'S / أزرق' });
    await expect(inventoryRow).toBeVisible({ timeout: 45_000 });
    await expect(page.locator('tr', { hasText: storeName }).filter({ hasText: 'M / أحمر' })).toContainText('Inactive variant');
    await page.screenshot({ path: `${shots}/variants-inventory-en.png`, fullPage: true });
  });

  test('5 · Arabic, right-to-left and dark mode read correctly, and a phone has no horizontal scroll', async () => {
    await page.evaluate(() => { localStorage.setItem('souq_lang', 'ar'); localStorage.setItem('souq_theme', 'dark'); });
    await page.goto(`/admin/products/${productId}/variants`);
    await expect.poll(() => page.evaluate(() => document.documentElement.dir)).toBe('rtl');
    await expect.poll(() => page.evaluate(() => document.documentElement.dataset.theme)).toBe('dark');
    await expect(page.getByRole('heading', { name: 'الخيارات والمتغيّرات', level: 2 })).toBeVisible();
    await expect(variantRow('L / أحمر')).toContainText('افتراضي');
    await expect(variantRow('M / أحمر')).toContainText('معطّل');
    // صيغة الصفر العربية: بلا تحديد يُقرأ الزرّ نصّاً لا مفتاح ترجمة (عيب ظهر في هذه الرحلة).
    await expect(page.getByRole('button', { name: 'إنشاء المتغيّرات' })).toBeDisabled();
    expect(await axe(page)).toEqual([]);
    await page.screenshot({ path: `${shots}/variants-ar-dark.png`, fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.reload();
    await expect(page.getByRole('heading', { name: 'الخيارات والمتغيّرات', level: 2 })).toBeVisible();
    await settle(page);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    expect(await axe(page)).toEqual([]);
    await page.screenshot({ path: `${shots}/variants-ar-phone.png`, fullPage: true });
  });
});
