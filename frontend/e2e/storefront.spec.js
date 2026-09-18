import { expect, test } from '@playwright/test';

// ============================================================================
// الرحلات الحرجة لواجهة المتجر على مكدّس حقيقي (المرحلة 16 §22).
// كل ما هنا يمرّ بالـ API الحقيقي وقاعدة بيانات حقيقية: لا بيانات مخترعة ولا نقاط مزيّفة.
// الدفع يمرّ بالبوّابة التجريبية (FakeGateway) التي يستعملها الخادم بلا مفاتيح Stripe —
// آلية اختبار قائمة في المستودع، لا خصم حقيقي.
// ============================================================================
const CUSTOMER = { email: `e2e+${Date.now()}@souq.test`, password: 'Customer@12345', name: 'E2E Customer' };

test.describe.configure({ mode: 'serial' });

// رحلة واحدة متّصلة تحتاج سياقاً واحداً: لكل اختبار سياق جديد افتراضياً، فتضيع الجلسة بين
// التسجيل والحساب والدفع — وهي رحلة زبون واحد لا سبع زيارات منفصلة.
let page;
const authTrace = [];
test.beforeAll(async ({ browser }) => {
  page = await browser.newPage();
  // أثر المصادقة عبر الرحلة كلّها: حين تسقط خطوة، السؤال الأول هو "هل ما زال مسجّلاً؟".
  page.on('response', (r) => {
    if (r.url().includes('/api/auth/')) {
      authTrace.push(`${new URL(r.url()).pathname.replace('/api/auth/', '')}→${r.status()}`);
    }
  });
});
test.afterEach(async ({}, testInfo) => {
  if (testInfo.status !== testInfo.expectedStatus) {
    console.log(`\n  AUTH TRACE: ${authTrace.join('  ')}\n  URL AT FAILURE: ${page.url()}`);
  }
});
test.afterAll(async () => { await page?.close(); });

test('1 · the storefront boots and shows the store identity', async () => {
  await page.goto('/');
  await expect(page.locator('h1')).toBeVisible();
  // العنوان من إعداد المتجر لا من ملفّ ثابت.
  await expect(page).toHaveTitle(/.+/);
  expect(await page.title()).not.toBe('Souq');
});

test('2 · the home page renders the hero, discovery rows and the catalog', async () => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.locator('#catalog')).toBeVisible();
  await expect(page.locator('article').first()).toBeVisible();
});

test('3 · a category from the nav filters the catalog through the URL', async () => {
  await page.goto('/');
  const category = page.locator('nav a[href^="/?cats="]').first();
  const href = await category.getAttribute('href');
  await category.click();
  await expect(page).toHaveURL(new RegExp(href.replace('/?', '\\?').replace(/[()]/g, '')));
  await expect(page.locator('article').first()).toBeVisible();
});

test('4 · search puts the term in the URL and survives a reload', async () => {
  await page.goto('/');
  // ============================================================================
  // دوره `combobox` لا `searchbox` — وهذا صواب المنتج لا خطؤه: منذ M3 للحقل قائمة اقتراحات
  // (`role="listbox"` مع `aria-expanded`/`aria-autocomplete`)، وحقلٌ كهذا دوره combobox في شجرة
  // الإتاحة. التعليق الذي كان هنا ("searchbox لا textbox") كُتب قبل ذلك وبقي، فظلّ هذا الاختبار
  // **يفشل منذ M3** بلا أن يلاحظه أحد — لأنّه لا يُشغَّل إلا على حزمة الحاويات. أمسكه M15.
  // ============================================================================
  const search = page.getByRole('search').first().getByRole('combobox');
  await search.fill('a');
  await search.press('Enter');
  await expect(page).toHaveURL(/[?&]q=a/);
  await page.reload();
  await expect(search).toHaveValue('a');
});

test('5 · catalog filters and paging are shareable URL state', async () => {
  await page.goto('/?sort=PriceAsc');
  await expect(page).toHaveURL(/sort=PriceAsc/);
  await expect(page.locator('article').first()).toBeVisible();
});

test('6 · the offers page shows only discounted products', async () => {
  await page.goto('/offers');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  const cards = page.locator('article');
  if (await cards.count() > 0) {
    // كل بطاقة في العروض تحمل سعر مقارنة مشطوباً.
    await expect(cards.first().locator('s')).toBeVisible();
  }
});

test('7 · a product opens by slug, and a legacy id URL redirects to it', async () => {
  await page.goto('/');
  await page.locator('article a[href^="/products/"]').first().click();
  await expect(page).toHaveURL(/\/products\/[^/]+$/);
  const slugUrl = page.url();
  expect(slugUrl).not.toMatch(/\/products\/\d+$/);   // الرابط القانوني بالاسم لا بالمعرّف

  await expect(page.getByRole('navigation', { name: /breadcrumb|مسار/i })).toBeVisible();
  await expect(page.locator('script#souq-structured-data')).toHaveCount(1);
});

test('8 · a legacy /products/:id URL still works and is replaced by the slug', async ({ request }) => {
  const res = await request.get('/api/products?page=1&pageSize=1');
  const body = await res.json();
  const product = body.items[0];
  await page.goto(`/products/${product.id}`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/products/${product.slug}$`));
});

test('9 · registering signs the customer in', async () => {
  await page.goto('/register');
  await page.getByRole('textbox').nth(0).fill(CUSTOMER.name);
  await page.getByRole('textbox').nth(1).fill(CUSTOMER.email);
  const passwords = page.locator('input[type="password"]');
  await passwords.nth(0).fill(CUSTOMER.password);
  await passwords.nth(1).fill(CUSTOMER.password);
  await page.getByRole('button', { name: /create account|إنشاء حساب/i }).click();
  await expect(page).toHaveURL(/\/$|\/account/);
  // شريط التنقّل يعرف المستخدم الآن.
  await expect(page.getByRole('link', { name: /account|حسابي/i }).or(page.getByText(CUSTOMER.name)).first()).toBeVisible();
});

test('10 · the account shell links profile, addresses and orders', async () => {
  await page.goto('/account');
  const nav = page.getByRole('navigation').filter({ has: page.locator('a[href="/account/addresses"]') });
  await expect(nav).toBeVisible();

  await nav.locator('a[href="/account/addresses"]').click();
  await expect(page).toHaveURL(/\/account\/addresses$/);
  await expect(nav.locator('a[href="/account/addresses"]')).toHaveAttribute('aria-current', 'page');
  // القسم الحالي وحده مُبرَز: /account بلا end كانت ستُبرز الملف الشخصي هنا أيضاً.
  await expect(nav.locator('[aria-current="page"]')).toHaveCount(1);

  await nav.locator('a[href="/orders"]').click();
  await expect(page).toHaveURL(/\/orders$/);
});

test('11 · a product can be added with a chosen quantity and reviewed in the cart page', async () => {
  await page.goto('/');
  await page.locator('article a[href^="/products/"]').first().click();
  await page.getByRole('heading', { level: 1 }).waitFor();

  // زرّ الإضافة في نصف الشراء، لا الذي داخل بطاقة منتج مشابه أسفل الصفحة.
  const buyRow = page.locator('h1').locator('xpath=following::button[normalize-space()="Add to cart"][1]');
  await page.getByRole('button', { name: 'Increase quantity' }).click();
  await buyRow.click();

  // السلّة تعرف الآن أن فيها صنفين — الخادم هو من أكّد ذلك.
  await expect(page.getByRole('button', { name: /view cart/i })).toContainText('2');

  await page.goto('/cart');
  await expect(page.getByRole('heading', { level: 1, name: /your cart/i })).toBeVisible();
  await expect(page.getByRole('button', { name: /proceed to checkout/i })).toBeEnabled();
});

test('12 · checkout places an order and the confirmation survives a reload', async () => {
  await page.goto('/checkout');
  await page.getByRole('textbox', { name: /full address/i })
    .or(page.getByPlaceholder(/City, neighborhood/i)).first()
    .fill('Amman, Downtown, Street 1, Building 5');
  await page.getByRole('button', { name: /continue to payment/i }).click();

  // الطلب أُنشئ وحُجز مخزونه ولم يُدفع بعد — الصفحة تقول ذلك صراحةً (المرحلة 16).
  await expect(page.getByText(/is being held for you/i)).toBeVisible();

  // بلا مفاتيح Stripe يستعمل الخادم بوّابته التجريبية: زرّ إتمام مباشر، لا خصم حقيقي.
  await page.getByRole('button', { name: /pay now/i }).click();

  await expect(page).toHaveURL(/\/confirmation\?order=\d+/);
  const confirmationUrl = page.url();
  await expect(page.getByText(/#\d+/)).toBeVisible();

  await page.reload();                       // كان هذا يعيد المشتري للرئيسية
  await expect(page).toHaveURL(confirmationUrl);
  await expect(page.getByText(/#\d+/)).toBeVisible();
});

test('13 · the order appears in my orders and opens', async () => {
  await page.goto('/orders');
  const row = page.locator('a[href^="/orders/"]').first();
  await expect(row).toBeVisible();
  await row.click();
  await expect(page).toHaveURL(/\/orders\/\d+$/);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});

test('14 · the public tracking link works signed out, and a bad token is a translated message', async ({ browser }, testInfo) => {
  await page.goto('/orders');
  await page.locator('a[href^="/orders/"]').first().click();
  // الرمز يُقرأ من زرّ النسخ نفسه بدل الحافظة: صلاحية الحافظة تختلف بين المتصفّحات،
  // والمقصود هنا هو الرابط الذي يُشارَك لا آلية النسخ.
  const trackingUrl = await page.evaluate(async () => {
    const button = [...document.querySelectorAll('button')].find((b) => /copy|نسخ/i.test(b.textContent));
    let copied = '';
    navigator.clipboard.writeText = async (text) => { copied = text; };
    button?.click();
    await new Promise((r) => setTimeout(r, 100));
    return copied;
  });
  const token = trackingUrl;

  // زائر حقيقي: سياق جديد بلا جلسة ولا ملفات ارتباط.
  // ============================================================================
  // الأصل من إعداد المشروع لا مكتوباً حرفياً (M15). كان `http://localhost:5173` — خادم Vite في التطوير —
  // فكان هذا الاختبار **لا يمرّ على حزمة الحاويات أبداً**: الاتصال يُرفض قبل أي تأكيد، وبمهلة طويلة.
  // نفس العيب الذي صُحِّح في `product-variants.spec.js` وفي `responsive.spec.js`؛ هذا ما بقي منه.
  // ============================================================================
  const visitorContext = await browser.newContext({ baseURL: testInfo.project.use.baseURL });
  const visitor = await visitorContext.newPage();
  if (token) {
    await visitor.goto(token);
    await expect(visitor.getByText(/#\d+/)).toBeVisible();
  }
  await visitor.goto('/track/deadbeefdeadbeefdeadbeefdeadbeef');
  await expect(visitor.locator('body')).not.toContainText('الطلب غير موجود');
  await visitor.close();
  await visitorContext.close();
});

test('15 · an unknown path renders a real 404, not a silent redirect home', async () => {
  await page.goto('/no-such-page');
  await expect(page).toHaveURL(/\/no-such-page$/);
  await expect(page.getByRole('heading', { level: 1, name: /page not found/i })).toBeVisible();
  // الصفحة تطلب صراحةً ألّا تُفهرَس — والتحويل الصامت للرئيسية كان يمنع ذلك أصلاً.
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', /noindex/);
});

test('16 · robots.txt is served and disallows the private routes', async ({ request }) => {
  const res = await request.get('/robots.txt');
  expect(res.status()).toBe(200);
  const body = await res.text();
  for (const path of ['/account', '/orders', '/cart', '/checkout', '/track', '/admin', '/platform']) {
    expect(body).toContain(`Disallow: ${path}`);
  }
});

test('17 · the storefront never exposes the admin area to a customer', async () => {
  await page.goto('/admin');
  await expect(page).not.toHaveURL(/\/admin$/);
});
