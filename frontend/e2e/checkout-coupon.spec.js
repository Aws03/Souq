import { expect, test } from '@playwright/test';

// ============================================================================
// الكوبون في متصفّح حقيقي، من لوحة التاجر إلى إجمالي الطلب (M8).
//
// **لم يكن للكوبون أي رحلة متصفّح قبل هذا الملفّ** — لا واحدة، مع أنه ميزة تمسّ المال، لها شاشات إدارة
// كاملة وخطوة في الدفع. زرّ "Apply" في صفحة الدفع لم يُضغط قطّ خارج اختبار مكوّن.
//
// وقيمته ارتفعت في M8 تحديداً: حُذف المُقيِّم الثاني (TD-06، `GET /api/coupons/apply`)، فصار
// `GET /api/basket/quote` الطريق **الوحيد** الذي يُسعَّر به رمز كوبون. ما كان يقيسه اختبارُ تكامل صار
// يستحقّ قياساً في المتصفّح: هل الرقم الذي رآه المتسوّق هو الرقم الذي سُجِّل عليه الطلب؟
//
// الاختبار الأخير هو الأهمّ: **ما رآه المتسوّق = ما سُجِّل في الطلب**. لا يكفي أن يظهر خصم؛ الواجب أن
// يكون الإجمالي المعروض هو نفسه إجمالي الطلب المُنشأ، وإلا فقد وُعد بسعر وحُصِّل غيره.
//
// يُشغَّل على حزمة الحاويات:
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/checkout-coupon.spec.js --project=desktop
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};
const stamp = Date.now().toString(36);

test.describe.configure({ mode: 'serial' });

// دخول مدير واحد للملفّ كلّه: حدّ الدخول 10 في الدقيقة، وكل تسجيل متسوّق يستهلك منه.
let cachedAdmin = null;
async function adminHeaders(request) {
  if (cachedAdmin) return cachedAdmin;
  const login = await request.post('/api/auth/login', { data: ADMIN });
  expect(login.ok(), 'تعذّر دخول المدير — اضبط SOUQ_E2E_ADMIN_EMAIL/PASSWORD').toBeTruthy();
  const body = await login.json();
  cachedAdmin = { Authorization: `Bearer ${body.accessToken ?? body.token}` };
  return cachedAdmin;
}

async function createProduct(request, price) {
  const headers = await adminHeaders(request);
  const categories = await request.get('/api/admin/categories', { headers });
  const created = await request.post('/api/products', {
    headers,
    data: {
      categoryId: (await categories.json())[0].id,
      slug: `m8-cpn-${stamp}-${price}`, price, stockQuantity: 20,
      translations: { ar: { name: `سلعة كوبون ${stamp} ${price}` }, en: { name: `Coupon Item ${stamp} ${price}` } },
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  return { id: (await created.json()).id, slug: `m8-cpn-${stamp}-${price}` };
}

// رمز الكوبون فريد لكل كوبون **ولكل تشغيل**: قاعدة التطوير تحمل كوبونات التشغيلات السابقة، ورمزٌ
// مكرّر يُرفض بـ 409 فيفشل الاختبار لسبب لا علاقة له بما يقيسه.
let couponSeq = 0;
const nextCode = () => `M8${stamp}${(couponSeq += 1)}`.replace(/[^a-z0-9]/gi, '').toUpperCase().slice(0, 12);

async function createCoupon(request, body) {
  const headers = await adminHeaders(request);
  const code = nextCode();
  const created = await request.post('/api/coupons', { headers, data: { code, ...body } });
  expect(created.status(), await created.text()).toBe(201);
  return code;
}

async function signUp(page, tag) {
  await page.goto('/register', { waitUntil: 'networkidle' });
  await page.getByRole('textbox').nth(0).fill(`M8 ${tag}`);
  await page.getByRole('textbox').nth(1).fill(`m8${tag}${stamp}@souq.test`);
  const passwords = page.locator('input[type="password"]');
  await passwords.nth(0).fill('Coupon@12345');
  await passwords.nth(1).fill('Coupon@12345');
  await page.getByRole('button', { name: /create account|إنشاء الحساب/i }).click();
  await page.waitForURL(/\/$|\/account/, { timeout: 30_000 });
}

async function addToCart(page, slug) {
  await page.goto(`/products/${slug}`, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { level: 1 }).waitFor();
  await page.locator('h1').locator('xpath=following::button[normalize-space()="Add to cart"][1]').click();
  await expect(page.getByRole('button', { name: /view cart/i })).toContainText('1');
}

async function fillAddress(page) {
  await page.getByRole('textbox', { name: /full address/i })
    .or(page.getByPlaceholder(/City, neighborhood/i)).first()
    .fill('Amman, Downtown, Street 1, Building 5');
}

const applyCode = async (page, code) => {
  await page.getByPlaceholder(/SAVE10/i).fill(code);
  await page.getByRole('button', { name: /^Apply$/ }).click();
};

// صفوف ملخّص الطلب في صفحة الدفع (OrderSummaryPanel): الأصناف تُقرأ بأسمائها لا بموضعها.
const summaryAmount = (page, row) => page.locator(`[class*="${row}"]`).last().locator('span').last();
const digits = (text) => text.replace(/[^\d]/g, '');

test('a percentage coupon shows its discount in the store currency, and the order records what was shown', async ({ browser, request }, testInfo) => {
  testInfo.setTimeout(180_000);
  const product = await createProduct(request, 40);
  const code = await createCoupon(request, { type: 'Percentage', value: 25 });

  const context = await browser.newContext();
  const page = await context.newPage();
  await signUp(page, 'pct');
  await addToCart(page, product.slug);
  await page.goto('/checkout', { waitUntil: 'networkidle' });
  await fillAddress(page);

  await applyCode(page, code);

  // 25% من 40 = 10، معروضاً بعملة المتجر (رمزها أو رمزها الحرفي — لا يُفترض أيّهما).
  const applied = page.getByText(/Coupon applied/i);
  await expect(applied).toBeVisible({ timeout: 20_000 });
  await expect(applied).toContainText(/10([.,]0+)?/);

  // سطر الخصم ظهر في الملخّص بمقداره، والإجمالي هبط إليه.
  await expect(summaryAmount(page, 'discountRow')).toHaveText(/10([.,]0+)?/);
  const shownTotal = await summaryAmount(page, 'totalRow').innerText();

  await page.getByRole('button', { name: /continue to payment/i }).click();
  await expect(page.getByText(/is being held for you/i)).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: /pay now/i }).click();
  await expect(page).toHaveURL(/\/confirmation\?order=\d+/, { timeout: 30_000 });

  // **ما رآه = ما سُجِّل.** 40 − 25% = 30، وهو ما يجب أن يكون الإجمالي المعروض في الدفع وما تعرضه
  // صفحة التأكيد للطلب المُنشأ. يُقارَن الرقم مجرَّداً من رمز العملة وفواصلها: المطلوب المبلغ نفسه لا
  // التنسيق نفسه.
  expect(digits(shownTotal), `الإجمالي المعروض في الدفع كان ${shownTotal}`).toContain('30');
  await expect(page.getByText(/30([.,]0+)?/).first()).toBeVisible({ timeout: 20_000 });

  await context.close();
});

test('an unknown code is refused with a readable reason, and leaves the total alone', async ({ browser, request }, testInfo) => {
  testInfo.setTimeout(180_000);
  const product = await createProduct(request, 12);

  const context = await browser.newContext();
  const page = await context.newPage();
  await signUp(page, 'bad');
  await addToCart(page, product.slug);
  await page.goto('/checkout', { waitUntil: 'networkidle' });
  await fillAddress(page);

  await applyCode(page, 'NO-SUCH-CODE');

  // رمز مرفوض **نتيجة داخل السلة** لا خطأ HTTP (خطّ التسعير الواحد)، فالمتسوّق يقرأ سبباً ويكمل.
  await expect(page.getByText(/coupon code is not valid/i)).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText(/Coupon applied/i)).toBeHidden();
  // والإجمالي لم يتحرّك: 12 كما كان، ولا سطر خصم أصلاً.
  await expect(summaryAmount(page, 'totalRow')).toHaveText(/12([.,]0+)?/);
  await expect(page.locator('[class*="discountRow"]')).toHaveCount(0);
  // ولا يزال الدفع ممكناً — الرفض لا يُعلّق الصفحة.
  await expect(page.getByRole('button', { name: /continue to payment/i })).toBeEnabled();

  await context.close();
});

test('a coupon below its minimum order is refused with its own reason, not a generic error', async ({ browser, request }, testInfo) => {
  testInfo.setTimeout(180_000);
  const product = await createProduct(request, 15);
  // حدّ أدنى 500 لا تبلغه سلة بـ 15: الرفض يجب أن يكون InvalidCoupon لا CouponNotFound.
  const code = await createCoupon(request, { type: 'FixedAmount', value: 5, minOrderAmount: 500 });

  const context = await browser.newContext();
  const page = await context.newPage();
  await signUp(page, 'min');
  await addToCart(page, product.slug);
  await page.goto('/checkout', { waitUntil: 'networkidle' });
  await fillAddress(page);

  await applyCode(page, code);

  // هذا هو ما كانت نقطة المعاينة المحذوفة (TD-06) تخطئ فيه: كانت تقيس الحدّ الأدنى على إجمالي فرعي
  // يرسله العميل، فتقبل كوبوناً يرفضه الدفع. الآن يُقاس على السلة الحقيقية، فالجوابان واحد.
  await expect(page.getByText(/can't be used for this order/i)).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText(/Coupon applied/i)).toBeHidden();

  await context.close();
});
