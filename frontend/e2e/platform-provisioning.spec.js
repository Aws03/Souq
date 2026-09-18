import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// تجهيز متجر من المنصّة إلى أوّل دخول لمديره — على مكدّس حقيقي، بمضيفين حقيقيين:
//   المنصّة على admin.localhost:5173، والمتجر الجديد على {slug}.localhost:5173 (وكيل Vite يمرّر ترويسة Host).
//
// المعيار هو معيار خروج المرحلة 18: "المالك يجهّز متجر عميل جديداً من أوّله إلى آخره في الواجهة". والرحلة لا
// تتوقّف عند نقرة "تفعيل": المدير يقبل دعوته على نطاق متجره ويدخل لوحته ويرى هويّته — وتوكن المالك يُرفض هناك،
// والمتجر الجديد لا يرى شيئاً من غيره. ثم يُؤرشف من المنصّة (بكتابة معرّفه) كي لا تتراكم متاجر مفتوحة في قاعدة QA.
//
// رابط الدعوة من سجلّ الـ API (مُرسِل البريد في التطوير يكتبه هناك): SOUQ_API_LOG يشير إلى ملف ذلك السجلّ.
// بلا المتغيّر تُتخطّى الرحلة كاملةً ولا تُدّعى — نصف رحلة تمرّ ليست دليلاً على التسليم.
//
// دخول واحد للمالك وصفحة واحدة: الخادم يحدّ الدخول بعشر محاولات في الدقيقة، وتحميلٌ كامل يقطع تجديد جلسة
// جارياً يترك رمزاً مُدوَّراً (انظر store-administration.spec.js).
// ============================================================================
// مضيف المنصّة وبيانات مالكها من البيئة، بقيمها الافتراضية (M11). كانا مكتوبين حرفيّاً — منفذ خادم
// التطوير وحساب بذرته — فلم يكن هذا الملفّ قابلاً للتشغيل على حزمة الحاويات إطلاقاً، وهو الملفّ الذي
// تطلبه المرحلة للتحقّق من التجهيز. (العلّة نفسها صُحِّحت في responsive.spec.js في M9 وفي خمسة ملفّات
// في M10؛ هذا آخرها.)
const platformOrigin = (base) => {
  const url = new URL(base);
  if (!url.hostname.startsWith('admin.')) url.hostname = `admin.${url.hostname}`;
  return url.origin;
};
const BASE = process.env.SOUQ_E2E_BASE_URL || 'http://localhost:5173';
// المنفذ يُشتقّ من baseURL أيضاً: كان 5173 مكتوباً في أربعة مواضع (مضيف المتجر الجديد، ومطابقة رابط
// الدعوة مرّتين)، وهو منفذ خادم التطوير — ورابط الدعوة يبنيه الخادم من FRONTEND_URL، فالمطابقة تفشل
// على أي حزمة أخرى. `PORT` يجعل الملفّ يعمل حيث تعمل الحزمة.
const PORT = new URL(BASE).port ? `:${new URL(BASE).port}` : '';
const PLATFORM = platformOrigin(BASE);
const OWNER = {
  email: process.env.SOUQ_E2E_OWNER_EMAIL || 'owner@souq.com',
  password: process.env.SOUQ_E2E_OWNER_PASSWORD || 'Owner@12345',
};
const API_LOG = process.env.SOUQ_API_LOG;
const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');

const stamp = Date.now().toString(36);
const slug = `qa-${stamp}`;
const host = `${slug}.localhost`;
const STORE = `http://${host}${PORT}`;
const storeName = `QA Provision ${stamp}`;
const adminEmail = `qa-boss-${stamp}@souq.test`;
const adminPassword = `Qa-Boss-${stamp}-Pass`;

test.describe.configure({ mode: 'serial' });
test.skip(!API_LOG, 'SOUQ_API_LOG must point to the running API\'s log to follow the invitation link');

/** @type {import('@playwright/test').Page} */
let page;
let storeId;

// التنبيهات والأدراج تظهر بحركة: فحص التباين في منتصفها يقيس لوناً ممزوجاً بالشفافية لا يراه أحد.
const axe = async (target) => {
  await target.evaluate(() => Promise.all(document.getAnimations().map((a) => a.finished.catch(() => null))));
  await target.addScriptTag({ content: axeSource });
  return target.evaluate(async () =>
    // @ts-ignore — axe يُحقن في الصفحة
    (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`));
};

const continueButton = () => page.getByRole('button', { name: /^(Continue|متابعة|Skip for now|تخطٍّ الآن)$/ });

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage({ locale: 'en-US' });
  await page.addInitScript(() => localStorage.setItem('souq_lang', 'en'));
  await page.goto(`${PLATFORM}/login`);
  await page.locator('input[type="email"]').fill(OWNER.email);
  await page.locator('input[type="password"]').fill(OWNER.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.getByRole('link', { name: /^Stores$/ }).waitFor({ timeout: 45_000 });
});

test.afterAll(async () => { await page?.close(); });

test('1 · the store is created closed to visitors, and the wizard opens on it', async () => {
  await page.getByRole('link', { name: /^Stores$/ }).click();
  await page.getByRole('link', { name: /New store/ }).click();

  await page.getByLabel('Store name').fill(storeName);
  await page.getByLabel('Slug').fill(slug);
  await page.getByLabel('Currency').fill('usd');
  await page.getByLabel('Default language').selectOption('en');

  // عملة ناقصة مثلاً لا تصل الخادم: التحقّق قبل الإرسال ثم الإنشاء الفعلي.
  const created = page.waitForResponse((r) => r.url().endsWith('/api/platform/tenants') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Create store' }).click();
  expect((await created).status()).toBe(201);

  await page.waitForURL(/\/platform\/stores\/\d+\/setup\/branding$/);
  storeId = page.url().match(/stores\/(\d+)\//)[1];
  await expect(page.getByText('Being set up').first()).toBeVisible();

  const config = await (await page.request.get(`${STORE}/api/storefront/config`)).json();
  expect(config.status).toBe('Provisioning');
  expect((await page.request.get(`${STORE}/api/products`)).status()).toBe(503);
});

test('2 · branding uses the store settings editor and saves to this store', async () => {
  await page.locator('#settings-colors-primary').waitFor();
  await page.locator('#settings-colors-primary').fill('#0B5D3B');
  const saved = page.waitForResponse((r) => r.url().endsWith(`/api/platform/tenants/${storeId}/settings`) && r.request().method() === 'PUT');
  await page.getByRole('button', { name: /^Save$/ }).click();
  expect((await saved).status()).toBe(204);
  await expect(page.getByText('All changes saved')).toBeVisible();
  await continueButton().click();
  await page.waitForURL(/setup\/domains$/);
});

test('3 · the platform host is refused as a domain; the store domain becomes primary', async () => {
  const field = page.getByLabel('Add a domain');
  await field.fill('admin.localhost');
  await page.getByRole('button', { name: 'Add domain' }).click();
  await expect(page.getByRole('alert').filter({ hasText: /platform/i })).toBeVisible();

  await field.fill(host.toUpperCase());
  await page.getByRole('button', { name: 'Add domain' }).click();
  const row = page.locator('li', { hasText: host });
  await expect(row.getByText('Primary')).toBeVisible();
  expect(await axe(page)).toEqual([]);
  await continueButton().click();
  await page.waitForURL(/setup\/modules$/);
});

test('4 · a disabled module is saved', async () => {
  await page.getByRole('checkbox', { name: /Wishlist/ }).uncheck();
  const saved = page.waitForResponse((r) => r.url().endsWith(`/api/platform/tenants/${storeId}/modules`));
  await page.getByRole('button', { name: /^Save$/ }).click();
  expect((await saved).status()).toBe(204);
  await continueButton().click();
  await page.waitForURL(/setup\/admin$/);
});

test('5 · the administrator is invited on the store\'s own domain', async () => {
  // المضيف معزول الاتجاه داخل الجملة (⁨…⁩)، فيُطابق بنمط لا بنصّ حرفي.
  await expect(page.getByText(new RegExp(`invitation link will open on \\W?${host.replace(/\./g, '\\.')}`))).toBeVisible();
  await page.getByLabel('Full name').fill('QA Boss');
  await page.getByLabel(/^Email$/).fill(adminEmail);
  const invited = page.waitForResponse((r) => r.url().endsWith(`/api/platform/tenants/${storeId}/admins`));
  await page.getByRole('button', { name: 'Send invitation' }).click();
  expect((await invited).status()).toBe(200);
  await expect(page.getByText('Invitation pending').first()).toBeVisible();
  await continueButton().click();
  await page.waitForURL(/setup\/review$/);
});

test('6 · activation warns about what is missing, then opens the store', async () => {
  await expect(page.getByText('Invitation sent; not accepted yet.')).toBeVisible();
  await page.getByRole('button', { name: 'Activate store' }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog.getByText(/No administrator has accepted/)).toBeVisible();
  expect(await axe(page)).toEqual([]);

  const activated = page.waitForResponse((r) => r.url().endsWith(`/api/platform/tenants/${storeId}/status`));
  await dialog.getByRole('button', { name: 'Activate store' }).click();
  expect((await activated).status()).toBe(204);
  await expect(dialog).toBeHidden();
  await expect(page.getByText('Active.', { exact: true })).toBeVisible();

  const config = await (await page.request.get(`${STORE}/api/storefront/config`)).json();
  expect(config.status).toBe('Active');
  expect(config.settings.branding.colors.primary).toBe('#0B5D3B');
  expect(config.modules).not.toContain('wishlist');
});

test('7 · the list shows the store with its domain and pending administrator', async () => {
  await page.getByRole('link', { name: /^Stores$/ }).click();
  await page.getByLabel('Search stores').fill(slug);
  const row = page.locator('tr', { hasText: storeName });
  await expect(row.getByText('Active')).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText(host)).toBeVisible();
  await expect(row.getByText('Invitation pending')).toBeVisible();
  expect(await axe(page)).toEqual([]);
});

test('8 · the administrator accepts on the store host and runs the store; the owner cannot', async ({ browser, request }) => {
  // الدعوة تمرّ بصندوق الصادر (المرحلة 14): تُرسل بعد الالتزام بدورة المُرسِل، لا في الطلب نفسه — فتُنتظر.
  const findLine = () => readFileSync(API_LOG, 'utf8').split('\n').reverse()
    .find((l) => l.includes(`${host}${PORT}/accept-invitation`));
  await expect.poll(findLine, { message: 'invitation link in the API log', timeout: 45_000, intervals: [1000] }).toBeTruthy();
  const line = findLine();
  const link = line.match(/https?:\/\/\S+accept-invitation\S+/)[0];
  expect(new URL(link).host).toBe(`${host}${PORT}`);

  const context = await browser.newContext({ locale: 'en-US' });
  await context.addInitScript(() => localStorage.setItem('souq_lang', 'en'));
  const boss = await context.newPage();
  await boss.goto(link);
  // صفحة القبول ترسمها هوية المتجر الجديد نفسها — تُنتظر حقولها لا تُفترض.
  await boss.getByLabel('New password').fill(adminPassword);
  await boss.getByLabel('Confirm password').fill(adminPassword);
  await boss.getByRole('button', { name: 'Activate account' }).click();
  await boss.waitForURL((url) => !url.pathname.includes('accept-invitation'), { timeout: 30_000 });

  if (!boss.url().includes('/admin')) {
    await boss.goto(`${STORE}/login`);
    await boss.locator('input[type="email"]').fill(adminEmail);
    await boss.locator('input[type="password"]').fill(adminPassword);
    await boss.getByRole('button', { name: /sign in/i }).click();
    await boss.waitForURL((url) => !url.pathname.includes('/login'));
  }
  await boss.goto(`${STORE}/admin`);
  await boss.getByRole('link', { name: /^Settings$/ }).click();
  await expect(boss.locator('#settings-colors-primary')).toHaveValue('#0B5D3B', { timeout: 30_000 });

  // الكتالوج فارغ: لا منتج من المتجر الافتراضي يعبر.
  await boss.getByRole('link', { name: /^Products$/ }).click();
  await expect(boss.getByText(/no matching products/i).first()).toBeVisible({ timeout: 30_000 });
  await context.close();

  // توكن المالك على مضيف المتجر: 401، على كل سطح إداري.
  const ownerLogin = await request.post(`${PLATFORM}/api/auth/login`, { data: OWNER });
  expect(ownerLogin.status()).toBe(200);
  const { accessToken } = await ownerLogin.json();
  for (const path of ['/api/admin/store/settings', '/api/admin/staff', '/api/admin/products']) {
    const res = await request.get(`${STORE}${path}`, { headers: { Authorization: `Bearer ${accessToken}` } });
    expect(res.status(), path).toBe(401);
  }
});

test('9 · the list now reports an active administrator; archiving needs the slug typed', async () => {
  await page.getByLabel('Search stores').fill('');
  await page.getByLabel('Search stores').fill(slug);
  const row = page.locator('tr', { hasText: storeName });
  await page.reload();
  await page.getByLabel('Search stores').fill(slug);
  await expect(row.getByText('1 active')).toBeVisible({ timeout: 15_000 });

  await row.getByRole('link', { name: storeName }).click();
  await expect(page.getByText('An administrator has accepted the invitation.')).toBeVisible();
  expect(await axe(page)).toEqual([]);

  // الوضع الداكن يُفحص بالأداة لا بالعين: تباين نصّ الأزرار على لون التمييز سقط هناك وحده.
  await page.getByRole('button', { name: /light and dark|التبديل بين/i }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  // الأزرار تنتقل بين الألوان بحركة قصيرة: قياس التباين في منتصفها يقيس لوناً لا يراه أحد مستقرّاً.
  await page.waitForTimeout(800);
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: /light and dark|التبديل بين/i }).click();

  await page.getByRole('button', { name: 'Archive store' }).click();
  const dialog = page.getByRole('alertdialog');
  const confirm = dialog.getByRole('button', { name: 'Archive store' });
  await expect(confirm).toBeDisabled();
  await dialog.getByRole('textbox').fill(slug);
  const archived = page.waitForResponse((r) => r.url().endsWith(`/api/platform/tenants/${storeId}/status`));
  await confirm.click();
  expect((await archived).status()).toBe(204);
  await expect(page.getByText('Archived: permanently closed. This cannot be undone.')).toBeVisible();
  await expect(page.getByRole('button', { name: /Activate store|Suspend store|Archive store/ })).toHaveCount(0);

  const config = await (await page.request.get(`${STORE}/api/storefront/config`)).json();
  expect(config.status).toBe('Archived');
});
