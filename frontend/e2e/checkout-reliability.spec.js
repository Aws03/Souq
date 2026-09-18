import { expect, test } from '@playwright/test';

// ============================================================================
// لا `networkidle` في هذا الملفّ (M15).
//
// `waitUntil: 'networkidle'` ينتظر سكون الشبكة نصف ثانية، فيعلّقه أي طلبٍ واحد يتأخّر — ومنه نداء
// `POST /api/auth/refresh` الذي تُطلقه الواجهة عند كل فتح صفحة حتى للزائر. قيس على الحزمة العاملة:
// الخادم يردّ 401 في نحو 25 مللي ثانية، ومع ذلك يبقى الطلب "معلّقاً" في نظر المتصفّح طويلاً، فلا يسكن
// شيء ويسقط الانتظار بمهلة. Playwright نفسه يصف هذا الانتظار بأنّه غير مستحبّ لهذا السبب.
//
// و`'load'` يكفي تماماً هنا: كل تنقّل بعده انتظارٌ صريح لعنصر، وPlaywright ينتظر العناصر من تلقائه.
// فالبديل ليس تخفيفاً للاختبار بل استبدال انتظارٍ عامّ متقلّب بانتظارٍ محدَّد.
// ============================================================================

// ============================================================================
// الدفع تحت ظروف معادية، في متصفّح حقيقي (M5).
//
// `storefront.spec.js` يقود الدفع السعيد إلى طلب مدفوع. هذا الملف يقود ما **لا** يسير كما يُخطَّط له، وهو ما
// يقرّر جودة متجر: سطر نفد بين فتح الصفحة وإرسالها، وضغطة مزدوجة على زرّ الدفع.
//
// كلاهما مُغطّى على مستوى الـ API (InventoryAndOrderTests، CheckoutIdempotencyTests). ما يضيفه المتصفّح هو
// السؤال الذي لا يجيبه أيّ منهما: **ماذا يرى المتسوّق؟** رسالة واضحة بسببها، أم شاشة عالقة أو خطأ عامّ.
//
// يُشغَّل على حزمة الحاويات أو خادم التطوير:
//   SOUQ_E2E_BASE_URL=http://localhost:8091 SOUQ_E2E_ADMIN_PASSWORD=... \
//     npx playwright test e2e/checkout-reliability.spec.js --project=desktop
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

test.describe.configure({ mode: 'serial' });

// ============================================================================
// ما يُنشئه هذا الملفّ يُنظَّف بعده (M15).
//
// رحلاته تحتاج منتجاتٍ نادرة (مخزون 1 أو 0) لتصنع سباق النفاد، وكانت تُترك في المتجر. والمتجر يعرض
// الأحدث أولاً، فكل تشغيلٍ يضع منتجاً نافداً على رأس الواجهة — و`storefront.spec.js` يُضيف "أول منتج"
// بكمّية اثنين، فيفشل. أي أنّ ملفّاً كان يكسر ملفّاً آخر ببقاياه، والعَرَض يظهر في الملفّ البريء.
//
// والحذف هنا أرشفة لا محو (`DELETE /api/products/{id}` يؤرشف): المنتج يغادر الواجهة وتبقى الطلبات
// التي تشير إليه سليمة — وهي بالضبط ما تُنشئه هذه الرحلات.
// ============================================================================
const createdProducts = [];

test.afterAll(async ({ request }, testInfo) => {
  if (createdProducts.length === 0) return;
  const headers = await adminHeaders(request, testInfo.project.use.baseURL).catch(() => null);
  if (!headers) return;
  for (const id of createdProducts) {
    await request.delete(`${testInfo.project.use.baseURL}/api/products/${id}`, { headers }).catch(() => {});
  }
});

// دخول واحد للملفّ كلّه: حدّ معدّل الدخول 10 في الدقيقة (DeveloperQualityGates)، وأربعة اختبارات تسجّل
// دخول المدير في كلّ منها تستهلكه مع تسجيلات المتسوّقين — فيفشل الملفّ لسبب لا علاقة له بما يقيسه.
let cachedAdminHeaders = null;
async function adminHeaders(request, base) {
  if (cachedAdminHeaders) return cachedAdminHeaders;
  const login = await request.post(`${base}/api/auth/login`, { data: ADMIN });
  expect(login.ok(), 'تعذّر دخول المدير — اضبط SOUQ_E2E_ADMIN_EMAIL/PASSWORD').toBeTruthy();
  const body = await login.json();
  cachedAdminHeaders = { Authorization: `Bearer ${body.accessToken ?? body.token}` };
  return cachedAdminHeaders;
}

// منتج بمخزون محدود، مُنشأ لهذا الاختبار وحده — لا يلمس الكتالوج المبذور ولا اختبارات أخرى.
async function createScarceProduct(request, base, stock) {
  const headers = await adminHeaders(request, base);
  const categories = await request.get(`${base}/api/admin/categories`, { headers });
  const categoryId = (await categories.json())[0].id;
  const slug = `m5-scarce-${Date.now()}`;
  const created = await request.post(`${base}/api/products`, {
    headers,
    data: {
      categoryId, slug, price: 19.5, stockQuantity: stock,
      translations: { ar: { name: `سلعة نادرة ${slug}` }, en: { name: `Scarce Item ${slug}` } },
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const id = (await created.json()).id;
  createdProducts.push(id);
  return { id, slug };
}

async function signUp(page, base, tag) {
  const stamp = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  await page.goto(`${base}/register`, { waitUntil: 'load' });
  await page.getByRole('textbox').nth(0).fill(`M5 ${tag}`);
  await page.getByRole('textbox').nth(1).fill(`m5${tag}+${stamp}@souq.test`);
  const passwords = page.locator('input[type="password"]');
  await passwords.nth(0).fill('Checkout@12345');
  await passwords.nth(1).fill('Checkout@12345');
  await page.getByRole('button', { name: /create account|إنشاء الحساب/i }).click();
  await page.waitForURL(/\/$|\/account/, { timeout: 20_000 });
}

async function addToCart(page, base, slug) {
  await page.goto(`${base}/products/${slug}`, { waitUntil: 'load' });
  await page.getByRole('heading', { level: 1 }).waitFor();
  await page.locator('h1').locator('xpath=following::button[normalize-space()="Add to cart"][1]').click();
  await expect(page.getByRole('button', { name: /view cart/i })).toContainText('1');
}

async function fillAddress(page) {
  await page.getByRole('textbox', { name: /full address/i })
    .or(page.getByPlaceholder(/City, neighborhood/i)).first()
    .fill('Amman, Downtown, Street 1, Building 5');
}

test('a line that sells out between page load and submit is refused with a reason, not a dead end', async ({ browser, request }, testInfo) => {
  testInfo.setTimeout(180_000);
  const base = testInfo.project.use.baseURL;
  const product = await createScarceProduct(request, base, 1);

  // متسوّقان على آخر قطعة. الأول يصل إلى صفحة الدفع ويقف هناك.
  const slowContext = await browser.newContext();
  const slow = await slowContext.newPage();
  await signUp(slow, base, 'slow');
  await addToCart(slow, base, product.slug);
  await slow.goto(`${base}/checkout`, { waitUntil: 'load' });
  await fillAddress(slow);

  // والثاني يشتريها فعلاً بينما الأول ما زال واقفاً — المخزون نفد تحت الصفحة المفتوحة.
  const fastContext = await browser.newContext();
  const fast = await fastContext.newPage();
  await signUp(fast, base, 'fast');
  await addToCart(fast, base, product.slug);
  await fast.goto(`${base}/checkout`, { waitUntil: 'load' });
  await fillAddress(fast);
  await fast.getByRole('button', { name: /continue to payment/i }).click();
  await expect(fast.getByText(/is being held for you/i)).toBeVisible();

  // الآن يُرسل الأول. الخادم يرفض — والمهمّ أنّ المتسوّق **يرى السبب**، لا زرّاً لا يفعل شيئاً.
  await slow.getByRole('button', { name: /continue to payment/i }).click();

  await expect(slow.getByText(/not available|only|out of stock|stock/i).first()).toBeVisible({ timeout: 20_000 });
  await expect(slow).toHaveURL(/\/checkout/, { timeout: 5_000 });
  // ولا طلب صار له: الصفحة لم تنتقل إلى دفع طلب لم يُنشأ.
  await expect(slow.getByText(/is being held for you/i)).toBeHidden();

  await slowContext.close();
  await fastContext.close();
});

test('a double click on continue does not fire checkout twice from the browser', async ({ browser, request }, testInfo) => {
  testInfo.setTimeout(180_000);
  const base = testInfo.project.use.baseURL;
  const product = await createScarceProduct(request, base, 5);

  const context = await browser.newContext();
  const page = await context.newPage();
  await signUp(page, base, 'double');
  await addToCart(page, base, product.slug);

  // كل نداء إنشاء طلب يُعدّ — الشبكة هي الحكم، لا ما تبدو عليه الصفحة.
  const attempts = [];
  page.on('request', (r) => {
    if (r.method() === 'POST' && new URL(r.url()).pathname === '/api/orders') attempts.push(r.url());
  });

  await page.goto(`${base}/checkout`, { waitUntil: 'load' });
  await fillAddress(page);

  // نقر مزدوج حقيقي: حدثان متتاليان بلا انتظار بينهما، كما يفعل الإصبع المتعجّل.
  await page.getByRole('button', { name: /continue to payment/i }).dblclick();
  await expect(page.getByText(/is being held for you/i)).toBeVisible({ timeout: 20_000 });

  // F-8 لم يُقرَّر بعد على مستوى الـ API (طلبان يُنشآن — CheckoutIdempotencyTests يقيس ذلك)، لكنّ الواجهة
  // يجب ألّا تكون هي مصدر التكرار: الزرّ يُعطَّل أثناء الإرسال، فالنقر المزدوج نداء واحد.
  expect(attempts.length, `عدد نداءات إنشاء الطلب: ${attempts.length}`).toBe(1);

  await context.close();
});

test('the happy path still reaches a paid order on the refactored checkout', async ({ browser, request }, testInfo) => {
  // بعد تقسيم CreateOrderHandler إلى ثلاث مراحل (TD-13): الطريق من السلة إلى طلب مدفوع كما كان تماماً.
  testInfo.setTimeout(180_000);
  const base = testInfo.project.use.baseURL;
  const product = await createScarceProduct(request, base, 3);

  const context = await browser.newContext();
  const page = await context.newPage();
  await signUp(page, base, 'happy');
  await addToCart(page, base, product.slug);
  await page.goto(`${base}/checkout`, { waitUntil: 'load' });
  await fillAddress(page);
  await page.getByRole('button', { name: /continue to payment/i }).click();
  await expect(page.getByText(/is being held for you/i)).toBeVisible();

  await page.getByRole('button', { name: /pay now/i }).click();
  await expect(page).toHaveURL(/\/confirmation\?order=\d+/);
  await expect(page.getByText(/#\d+/)).toBeVisible();

  await context.close();
});

test('an admin refunds a paid order through the admin UI, and the order shows it', async ({ browser, request }, testInfo) => {
  // معيار M6 للمتصفّح: طلب مدفوع **واسترداد كامل** بالشكلين المُفعَّلين اليوم للصلاحية والتوجيه — أي
  // store.payments.manage (المدير) عبر حساب النشر. الاسترداد مُغطّى بالتكامل، وغير مُغطّى في الواجهة إطلاقاً
  // قبل هذا الاختبار: لا شيء كان يثبت أنّ التاجر يستطيع فعلاً إرجاع مال زبونه من لوحته.
  testInfo.setTimeout(180_000);
  const base = testInfo.project.use.baseURL;
  const product = await createScarceProduct(request, base, 2);

  // زبون يشتري ويدفع.
  const shopper = await browser.newContext();
  const page = await shopper.newPage();
  await signUp(page, base, 'refundee');
  await addToCart(page, base, product.slug);
  await page.goto(`${base}/checkout`, { waitUntil: 'load' });
  await fillAddress(page);
  await page.getByRole('button', { name: /continue to payment/i }).click();
  await page.getByRole('button', { name: /pay now/i }).click();
  await expect(page).toHaveURL(/\/confirmation\?order=\d+/);
  // اللوحة تعرض **رقم الطلب** لا معرّفه، وهو ما تعرضه صفحة التأكيد أيضاً.
  const orderNumber = (await page.getByText(/#\d+/).first().innerText()).match(/#(\d+)/)[1];
  await shopper.close();

  // ثم المدير يستردّ من لوحته.
  const back = await browser.newContext();
  const admin = await back.newPage();
  await admin.goto(`${base}/login`, { waitUntil: 'load' });
  await admin.locator('input[type="email"]').first().fill(ADMIN.email);
  await admin.locator('input[type="password"]').first().fill(ADMIN.password);
  await admin.getByRole('button', { name: /sign in|دخول/i }).click();
  await admin.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 45_000 });

  await admin.goto(`${base}/admin/orders`, { waitUntil: 'load' });
  // الصفّ يُفتح من قائمة إجراءاته لا بالنقر على نصّه.
  const row = admin.locator('tr', { hasText: `#${orderNumber}` }).first();
  await row.getByRole('button', { name: /actions|إجراءات|more|المزيد/i }).or(row.getByRole('button')).last().click();
  await admin.getByRole('menuitem', { name: /view|عرض/i }).click();

  const drawer = admin.getByRole('dialog');
  await expect(drawer.getByText(/^Payment$/)).toBeVisible();

  await drawer.getByRole('button', { name: /^Refund$/ }).click();
  // بلا مبلغ = الباقي كلّه، وهو ما يقوله التلميح تحت الحقل.
  await drawer.getByRole('button', { name: /^Refund$/ }).last().click();

  // البوّابة التجريبية تُنجح الاسترداد فوراً. يُؤكَّد **المبلغ** لا مجرّد كلمة "مُسترَدّ": الثمن كلّه رجع
  // (19.5 هو سعر المنتج المُنشأ أعلاه)، وهو الفرق بين "تغيّرت حالة" و"خرج مال".
  await expect(drawer.getByText(/Refunded\s+\S+\s*19[.,]5/i)).toBeVisible({ timeout: 20_000 });
  await back.close();
});
