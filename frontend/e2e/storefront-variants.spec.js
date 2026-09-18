import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// اختيار المتغيّر في واجهة المتجر على مكدّس حقيقي (ProductVariants.md V3، ADR-0041) — كما يتسوّق الزبون:
//
//   1. بطاقة المنتج تقول "ابتداءً من" وتقود لصفحته (لا زرّ إضافة يفترض مقاساً).
//   2. الصفحة: الاختيار صريح، والنافد معروض معطّلاً، والمستحيل مع الاختيار معطّل كذلك، والسعر والمتاح يتبعان المختار،
//      والرابط يحمل المتغيّر فيصمد عبر إعادة التحميل.
//   3. مقاسان من المنتج نفسه = سطران في السلة بوصفيهما ⇒ دفع ⇒ طلب ⇒ صفحة الطلب وبريده.
//   4. المخزون يتغيّر بين تحميل الصفحة والإضافة: الخادم يرفض، والواجهة تقول السبب.
//   5. نفد كل ما يُعرض: المنتج يبقى معروضاً غير متاح.
//   6. بالعربية (RTL) وفي الوضع الداكن بلا مخالفة إتاحة، ثم على عرض هاتف بلا تمرير أفقي.
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

test.describe('اختيار المتغيّر في المتجر', () => {
  /** @type {import('@playwright/test').Page} */
  let page;
  let token;
  let productId;
  let variants = {};
  const productName = `QA Variant Tee ${stamp}`;
  const storeName = `QA قميص متغيّرات ${stamp}`;

  const authed = (extra = {}) => ({ headers: { Authorization: `Bearer ${token}` }, ...extra });

  test.beforeAll(async ({ browser, request }) => {
    page = await browser.newPage({ locale: 'en-US' });
    // مرّة واحدة: لو كُتبت في كل تحميل لأعادت الإنجليزية فوق العربية التي يبدّلها آخر فحص.
    await page.addInitScript(() => {
      if (sessionStorage.getItem('qa-prefs')) return;
      localStorage.setItem('souq_lang', 'en');
      localStorage.setItem('souq_theme', 'light');
      sessionStorage.setItem('qa-prefs', '1');
    });

    token = (await (await request.post('/api/auth/login', { data: ADMIN })).json()).accessToken;
    const category = await request.post('/api/categories', authed({
      data: {
        slug: `qa-variant-tee-${stamp}`,
        translations: { en: { name: `QA Variant Tees ${stamp}` }, ar: { name: `QA قمصان ${stamp}` } },
        sortOrder: 997, isActive: true,
      },
    }));
    expect(category.status()).toBe(201);
    const categoryId = (await category.json()).id;

    // منتج بسعر 20 ومخزون 5 (المتغيّر الافتراضي)، ثم خيارَي المقاس واللون بالعربية والإنجليزية.
    const created = await request.post('/api/products', authed({
      data: {
        categoryId, price: 20, stockQuantity: 5, status: 'Active',
        translations: { en: { name: productName, description: 'A QA tee' }, ar: { name: storeName, description: 'قميص اختبار' } },
      },
    }));
    expect(created.status()).toBe(201);
    productId = (await created.json()).id;

    const names = (ar, en) => ({ ar, en });
    const options = await request.put(`/api/admin/products/${productId}/options`, authed({
      data: {
        options: [
          { names: names('المقاس', 'Size'), existingVariantsValue: 0,
            values: [{ names: names('S', 'S') }, { names: names('M', 'M') }, { names: names('L', 'L') }] },
          { names: names('اللون', 'Colour'), existingVariantsValue: 0,
            values: [{ names: names('أحمر', 'Red') }, { names: names('أزرق', 'Blue') }] },
        ],
      },
    }));
    expect(options.status()).toBe(204);

    const admin = await (await request.get(`/api/admin/products/${productId}`, authed())).json();
    const valueId = (option, value) =>
      admin.options.find((o) => o.names.en === option).values.find((v) => v.names.en === value).id;
    variants.smallRed = admin.variants.find((v) => v.isDefault).id;

    // S/أزرق بسعر 20 ونفد، وL/أحمر بسعر 25 ومتاح 4 — وM بلا متغيّر (تركيبة لم يُنشئها التاجر).
    const batch = await request.post(`/api/admin/products/${productId}/variants`, authed({
      data: {
        variants: [
          { optionValueIds: [valueId('Size', 'S'), valueId('Colour', 'Blue')], price: 20, initialStock: 0 },
          { optionValueIds: [valueId('Size', 'L'), valueId('Colour', 'Red')], price: 25, initialStock: 4 },
        ],
      },
    }));
    expect(batch.status()).toBe(200);
    const ids = (await batch.json()).ids;
    [variants.smallBlue, variants.largeRed] = ids;
  });

  test.afterAll(async ({ request }) => {
    if (productId) await request.delete(`/api/products/${productId}`, authed());
    await page?.close();
  });

  const chip = (name) => page.getByRole('radio', { name: new RegExp(`^${name}`) });
  // زرّ الشراء في صفّ الشراء وحده: صفحة المنتج تحمل أيضاً بطاقات "ذات صلة" لكلٍّ زرّ إضافة.
  const buyButton = () => page.locator('[class*="buyRow"]').getByRole('button', { name: /Add to cart|Out of stock|أضف|نفد/ });

  test('1 · the card prices "from" and sends the shopper to the product page', async () => {
    await page.goto(`/?cats=${await categoryOf(page, productId)}`);
    const card = page.locator('article', { hasText: productName });
    await card.waitFor({ timeout: 45_000 });

    await expect(card).toContainText('From');
    await expect(card.getByRole('link', { name: 'Choose options' })).toBeVisible();
    await expect(card.getByRole('button', { name: 'Add to cart' })).toHaveCount(0);

    await card.getByRole('link', { name: 'Choose options' }).click();
    await expect(page.getByRole('heading', { level: 1, name: productName })).toBeVisible();
  });

  test('2 · the choice is explicit, sold out is shown disabled, and the link keeps it', async () => {
    // M لا متغيّر له: لا يُعرض إطلاقاً. S وL يُعرضان.
    await expect(chip('S')).toBeVisible();
    await expect(chip('L')).toBeVisible();
    await expect(page.getByRole('radio', { name: /^M/ })).toHaveCount(0);
    await expect(buyButton()).toBeDisabled();
    await expect(page.getByRole('status')).toContainText('Choose');

    await chip('S').click();
    await expect(chip('Blue')).toBeDisabled();            // S / أزرق نفد
    await expect(chip('Red')).toBeEnabled();
    await chip('Red').click();

    await expect(buyButton()).toBeEnabled();
    await expect(page).toHaveURL(new RegExp(`variant=${variants.smallRed}$`));

    // تبديل المقاس يُبقي اللون المختار ويُحدّث السعر (25 بدل 20).
    await chip('L').click();
    await expect(chip('Red')).toBeChecked();
    await expect(page).toHaveURL(new RegExp(`variant=${variants.largeRed}$`));

    // إعادة التحميل تُبقي المتغيّر نفسه مختاراً (الرابط يحمله).
    await page.reload();
    await expect(chip('L')).toBeChecked();
    await expect(chip('Red')).toBeChecked();
    await page.screenshot({ path: `${shots}/storefront-variants-en.png`, fullPage: true });
  });

  test('3 · two sizes are two basket lines, and their labels reach the order and its email', async ({ request }) => {
    const customer = { email: `qa-variant-${stamp}@souq.test`, password: 'Customer@123', fullName: 'QA Variant' };
    const registered = await page.request.post('/api/auth/register', { data: customer });
    expect([200, 201]).toContain(registered.status());
    await page.goto('/login');
    await page.locator('input[type="email"]').first().fill(customer.email);
    await page.locator('input[type="password"]').first().fill(customer.password);
    await page.getByRole('button', { name: /sign in|دخول/i }).click();
    await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 45_000 });

    await page.goto(`/products/${productId}?variant=${variants.largeRed}`);
    await buyButton().click();
    await page.goto(`/products/${productId}?variant=${variants.smallRed}`);
    await buyButton().click();

    await page.goto('/cart');
    // سطران لمنتج واحد بوصفيهما بلغة الواجهة (الخادم يرسل الوصف بكل لغات المنتج)، وكلٌّ بسعر متغيّره.
    await expect(page.getByText('L / Red')).toBeVisible();
    await expect(page.getByText('S / Red')).toBeVisible();
    await expect(page.getByRole('button', { name: /remove/i })).toHaveCount(2);
    await expect(page.getByText('JOD 25.000').first()).toBeVisible();

    await page.getByRole('button', { name: /proceed to checkout/i }).click();
    await page.waitForURL(/\/checkout/, { timeout: 45_000 });
    await expect(page.locator('aside')).toContainText('L / Red');

    // الطلب يُنشأ ويُدفع من الشاشة نفسها (البوّابة التجريبية في التطوير) — لا استدعاء API بالتوكن.
    await page.getByRole('textbox', { name: /full address/i })
      .or(page.getByPlaceholder(/City, neighborhood/i)).first()
      .fill('Amman, Downtown, Street 1, Building 5');
    await page.getByRole('button', { name: /continue to payment/i }).click();
    await page.getByRole('button', { name: /pay now/i }).click();
    await expect(page).toHaveURL(/\/confirmation\?order=\d+/, { timeout: 45_000 });
    const orderId = new URL(page.url()).searchParams.get('order');

    // صفحة الطلب: لقطة الوصف بلغة المتجر (ما جُمّد لحظة الشراء لا يُترجَم بعدها).
    await page.goto(`/orders/${orderId}`);
    await expect(page.getByText('L / أحمر')).toBeVisible();
    await expect(page.getByText('S / أحمر')).toBeVisible();
    await page.screenshot({ path: `${shots}/storefront-variants-order.png`, fullPage: true });

    const order = await (await request.get(`/api/orders/${orderId}`, authed())).json();
    expect(order.items.map((i) => i.variantLabel).sort()).toEqual(['L / أحمر', 'S / أحمر']);

    // بريد التأكيد أُرسل فعلاً (محوّل السجل في التطوير يسجّل القالب والمستلم لا الجسم — وصف المتغيّر في جسمه
    // يفحصه StorefrontVariantTests على المُرسِل في الذاكرة).
    const log = process.env.SOUQ_API_LOG;
    if (log) {
      await expect.poll(() => readFileSync(log, 'utf8').includes('OrderConfirmed'), { timeout: 30_000 }).toBe(true);
    }
  });

  test('4 · stock that runs out between page load and add is refused by the server', async ({ request }) => {
    await page.goto(`/products/${productId}?variant=${variants.largeRed}`);
    await expect(buyButton()).toBeEnabled();

    // نفد المتغيّر بعد أن رآه المتسوّق: الصفحة بيدها متاح قديم، والخادم يرفض بما يملكه الآن.
    const row = (await (await request.get('/api/admin/inventory?pageSize=100', authed())).json())
      .items.find((i) => i.variantId === variants.largeRed);
    expect(row.available).toBeGreaterThan(0);
    const drain = await request.post(`/api/admin/inventory/variants/${variants.largeRed}/adjustments`,
      authed({ data: { delta: -row.available, reason: 'QA drain' } }));
    expect(drain.status()).toBe(200);

    // نقرة على الواجهة القديمة كما هي (بلا انتظار حالة الزرّ: الصفحة قد تكون حدّثت نفسها بينهما).
    await buyButton().dispatchEvent('click');

    // النتيجة التي تهمّ: لا شيء دخل السلّة — الخادم هو من يقرّر، لا حالة الصفحة.
    await page.goto('/cart');
    await expect(page.getByText('L / Red')).toHaveCount(0);
    await expect(page.getByText(/your cart is empty|cart is empty|لا أصناف|سلّتك فارغة/i).first()).toBeVisible();
  });

  test('5 · a product whose every variant is sold out stays listed as unavailable', async ({ request }) => {
    for (const [name, id] of Object.entries(variants)) {
      const stock = await (await request.get(`/api/admin/inventory?pageSize=100`, authed())).json();
      const row = stock.items.find((i) => i.variantId === id);
      if (row && row.available > 0) {
        const response = await request.post(`/api/admin/inventory/variants/${id}/adjustments`,
          authed({ data: { delta: -row.available, reason: `QA drain ${name}` } }));
        expect(response.status()).toBe(200);
      }
    }

    await page.goto(`/products/${productId}`);
    await expect(buyButton()).toBeDisabled();
    await expect(chip('S')).toBeDisabled();
    await expect(page.getByRole('status')).toHaveCount(0);

    await page.goto(`/?cats=${await categoryOf(page, productId)}`);
    await expect(page.locator('article', { hasText: productName })).toContainText('Out of stock');
  });

  test('6 · Arabic, right-to-left and dark mode read correctly, and a phone has no horizontal scroll', async ({ request }) => {
    // نُعيد التزويد كي يُقرأ الاختيار في حالته الطبيعية.
    for (const id of [variants.smallRed, variants.largeRed]) {
      expect((await request.post(`/api/admin/inventory/variants/${id}/adjustments`,
        authed({ data: { delta: 4, reason: 'QA restock' } }))).status()).toBe(200);
    }

    // التبديل من الواجهة نفسها (كما يفعل الزائر): زرّ اللغة ثم زرّ الوضع في الشريط العلوي.
    await page.goto(`/products/${productId}`);
    await page.getByRole('button', { name: 'Toggle language' }).click();
    await expect.poll(() => page.evaluate(() => document.documentElement.dir)).toBe('rtl');
    await page.getByRole('button', { name: 'التبديل بين الفاتح والداكن' }).click();
    await expect.poll(() => page.evaluate(() => document.documentElement.dir)).toBe('rtl');
    await expect.poll(() => page.evaluate(() => document.documentElement.dataset.theme)).toBe('dark');

    await expect(page.getByRole('heading', { level: 1, name: storeName })).toBeVisible();
    await expect(page.getByRole('radio', { name: /^أحمر/ })).toBeVisible();
    await page.getByRole('radio', { name: /^S/ }).click();
    await page.getByRole('radio', { name: /^أحمر/ }).click();
    await expect(buyButton()).toBeEnabled();
    expect(await axe(page)).toEqual([]);
    await page.screenshot({ path: `${shots}/storefront-variants-ar-dark.png`, fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.reload();
    await expect(page.getByRole('radio', { name: /^أحمر/ })).toBeVisible();
    await settle(page);
    expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(1);
    expect(await axe(page)).toEqual([]);
    await page.screenshot({ path: `${shots}/storefront-variants-ar-phone.png`, fullPage: true });
  });
});

// الفئة من عقد المنتج العام (لا حاجة لقاعدة البيانات في رحلة المتصفّح).
async function categoryOf(page, productId) {
  const product = await (await page.request.get(`/api/products/${productId}`)).json();
  return product.categoryId;
}
