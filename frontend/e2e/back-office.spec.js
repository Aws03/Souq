import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// إكمال المكتب الخلفي على مكدّس حقيقي: تأكيدات الحذف في لوحة المتجر، وحسابات المنصّة، وسجلّ النشاط.
//
//   • التأكيد: الإلغاء (Escape) لا يُرسل شيئاً، والتأكيد يحذف فعلاً، ورفض الخادم (فئة فيها منتجات) يُقرأ داخل الحوار
//     والفئة باقية. حوار فوق درج المنتج يُغلق وحده.
//   • الحسابات: المالك يدعو مشرفاً؛ المشرف يقبل على مضيف المنصّة ويدخل فلا يرى "الحسابات" ولا يصل إليها؛ المالك يوقفه
//     بعد تأكيد فتسقط جلسته.
//   • السجلّ: الإيقاف نفسه سطرٌ يُعثر عليه بمرشّح النشاط، وبالحساب، وبالمتجر من صفحته، وبالصفحة التالية من الخادم —
//     ثم بالعربية وفي الوضع الداكن بلا مخالفة إتاحة.
//
// الفئة التي تُحذف تُنشأ لهذه الرحلة وحدها؛ والمشرف المدعوّ يبقى موقوفاً (لا حذف للحسابات في الخادم) ببريد فريد.
// دخول واحد لكل حساب: الخادم يحدّ الدخول بعشر محاولات في الدقيقة. SOUQ_API_LOG لرابط الدعوة (بلا المتغيّر يُتخطّى
// قسم الحسابات ولا يُدّعى).
// ============================================================================
// بيانات المدير من البيئة بقيمها الافتراضية (M10): كانت مكتوبةً حرفيّاً، فلم يكن هذا الملفّ قابلاً
// للتشغيل إلا على حزمةٍ مبذورةٍ بهذين تحديداً — وعلى غيرها يفشل عند الدخول بمهلةٍ منتهية، لا برسالةٍ
// تقول السبب. (العلّة نفسها التي كانت في مضيف المنصّة داخل responsive.spec.js، صُحِّحت في M9.)
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};
// من البيئة بقيمهما الافتراضية (M11) — آخر موضعين مكتوبين حرفيّاً في e2e.
const OWNER = {
  email: process.env.SOUQ_E2E_OWNER_EMAIL || 'owner@souq.com',
  password: process.env.SOUQ_E2E_OWNER_PASSWORD || 'Owner@12345',
};
const PLATFORM = (() => {
  const url = new URL(process.env.SOUQ_E2E_BASE_URL || 'http://localhost:5173');
  if (!url.hostname.startsWith('admin.')) url.hostname = `admin.${url.hostname}`;
  return url.origin;
})();
const API_LOG = process.env.SOUQ_API_LOG;
const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');
const stamp = Date.now().toString(36);

test.describe.configure({ mode: 'serial' });

// الأدراج والحوارات تنزلق وتظهر تدريجياً: فحص التباين في منتصف الحركة يقيس لوناً ممزوجاً بالشفافية لا يراه أحد.
// حركةٌ لا تنتهي لا يُنتظَر انتهاؤها — وإلّا عُلِّق الانتظار إلى أن تنفد المهلة. شريط الإعلان
// يمرّ إلى الأبد على كل صفحة متجر، والهيكل يلمع، والدوّار يدور: ثلاثتها `iterations: Infinity`.
const settle = (target) => target.evaluate(() => Promise.all(document.getAnimations()
    .filter((a) => a.effect?.getTiming?.().iterations !== Infinity)
    .map((a) => a.finished.catch(() => null))));

const axe = async (target, include = null) => {
  await settle(target);
  await target.addScriptTag({ content: axeSource });
  return target.evaluate(async (selector) =>
    // @ts-ignore — axe يُحقن في الصفحة
    (await window.axe.run(selector ? { include: [selector] } : document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`), include);
};

const signIn = async (page, base, { email, password }) => {
  await page.goto(`${base}/login`);
  await page.locator('input[type="email"]').first().fill(email);
  await page.locator('input[type="password"]').first().fill(password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 45_000 });
};

test.describe('تأكيدات لوحة المتجر', () => {
  /** @type {import('@playwright/test').Page} */
  let page;
  let token;

  test.beforeAll(async ({ browser, request }) => {
    page = await browser.newPage({ locale: 'en-US' });
    await page.addInitScript(() => localStorage.setItem('souq_lang', 'en'));
    await signIn(page, '', ADMIN);
    // عميل HTTP للتهيئة والتحقّق وحده (فئة الرحلة ومعرفة ما في الخادم) — الشاشة تُستعمل كما يستعملها التاجر.
    token = (await (await request.post('/api/auth/login', { data: ADMIN })).json()).accessToken;
  });

  test.afterAll(async () => { await page?.close(); });

  const authed = (extra = {}) => ({ headers: { Authorization: `Bearer ${token}` }, ...extra });

  test('حذف فئة: Escape لا يُرسل، والتأكيد يحذف، ورفض الخادم يبقى داخل الحوار', async ({ request }) => {
    const slug = `qa-confirm-${stamp}`;
    const name = `QA Confirm ${stamp}`;
    const created = await request.post('/api/categories', authed({
      data: { slug, translations: { en: { name }, ar: { name } }, parentId: null, sortOrder: 999, isActive: false },
    }));
    expect(created.status()).toBe(201);
    const { id } = await created.json();

    await page.goto('/admin');
    await page.getByRole('link', { name: /^Categories$/ }).click();
    const row = page.locator('tr', { hasText: name });
    await row.waitFor({ timeout: 45_000 });

    const openDelete = async (target) => {
      await target.getByRole('button').last().click();
      await page.getByRole('menuitem', { name: 'Delete' }).click();
      return page.getByRole('alertdialog');
    };

    let deletes = 0;
    page.on('request', (r) => { if (r.method() === 'DELETE' && r.url().includes('/api/categories/')) deletes += 1; });

    const dialog = await openDelete(row);
    // الاسم معزول الاتجاه (U+2068…U+2069) كي لا يقلب اسمٌ عربي علامات الجملة الإنجليزية.
    await expect(dialog).toHaveAccessibleName(new RegExp(`^Delete the category "\u2068?${name}\u2069?"\\?$`));
    await expect(dialog).toHaveAccessibleDescription(/can't be undone/);
    expect(await axe(page, '[role="alertdialog"]')).toEqual([]);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('alertdialog')).toHaveCount(0);
    await expect(row).toBeVisible();
    expect(deletes).toBe(0);

    await (await openDelete(row)).getByRole('button', { name: 'Delete category' }).click();
    await expect(page.getByRole('alertdialog')).toHaveCount(0);
    await expect(row).toHaveCount(0);
    expect(deletes).toBe(1);
    // القائمة الإدارية (تشمل المعطّلة) — العامة لا تعرض فئة معطّلة أصلاً فلا تُثبت شيئاً.
    const remaining = await (await request.get('/api/admin/categories', authed())).json();
    expect(remaining.some((c) => c.id === id)).toBe(false);

    // فئة فيها منتجات: الخادم يرفض (CategoryInUse)، والرفض يُقرأ داخل الحوار، والفئة باقية.
    const products = await (await request.get('/api/admin/products?pageSize=1', authed())).json();
    test.skip(products.items.length === 0, 'the store has no product to hold a category');
    const categories = await (await request.get('/api/admin/categories', authed())).json();
    const used = categories.find((c) => c.id === products.items[0].categoryId);
    const usedName = used.translations?.en?.name ?? used.name;
    const usedRow = page.locator('tr', { hasText: usedName }).first();
    const refusal = await openDelete(usedRow);
    await refusal.getByRole('button', { name: 'Delete category' }).click();
    await expect(refusal.getByRole('alert')).toBeVisible();
    await expect(refusal).toBeVisible();
    await refusal.getByRole('button', { name: 'Cancel' }).click();
    await expect(usedRow).toBeVisible();
  });

  test('إزالة صورة: الحوار فوق درج المنتج، Escape يغلقه وحده، والتأكيد يزيلها فعلاً', async ({ request }) => {
    // منتج مسودّة لهذه الرحلة بصورة PNG صغيرة (يُفحص نوع الملف من ترويسته) — يُؤرشف في النهاية.
    const categories = await (await request.get('/api/categories', authed())).json();
    const name = `QA Image ${stamp}`;
    const created = await request.post('/api/products', authed({
      data: { categoryId: categories[0].id, translations: { en: { name }, ar: { name } }, price: 5, stockQuantity: 0 },
    }));
    expect(created.status()).toBe(201);
    const { id } = await created.json();
    const png = Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0]);
    const uploaded = await request.post(`/api/products/${id}/image`, authed({
      multipart: { file: { name: 'qa.png', mimeType: 'image/png', buffer: png } },
    }));
    expect(uploaded.ok()).toBe(true);

    try {
      await page.getByRole('link', { name: /^Products$/ }).click();
      await page.getByPlaceholder(/Search by name/).fill(name);
      const row = page.locator('tr', { hasText: name }).first();
      await row.waitFor({ timeout: 45_000 });
      await row.getByRole('button').last().click();
      await page.getByRole('menuitem', { name: 'Edit' }).click();

      const drawer = page.getByRole('dialog');
      const remove = drawer.getByRole('button', { name: 'Remove' });
      await expect(remove).toHaveCount(1, { timeout: 30_000 });
      await remove.click();
      await expect(page.getByRole('alertdialog')).toBeVisible();
      expect(await axe(page, '[role="alertdialog"]')).toEqual([]);
      await page.keyboard.press('Escape');
      await expect(page.getByRole('alertdialog')).toHaveCount(0);
      await expect(drawer).toBeVisible();
      await expect(remove).toHaveCount(1);

      const removed = page.waitForResponse((r) => r.url().includes(`/api/admin/products/${id}/images/`) && r.request().method() === 'DELETE');
      await remove.click();
      await page.getByRole('alertdialog').getByRole('button', { name: 'Remove image' }).click();
      expect((await removed).status()).toBe(204);
      await expect(page.getByRole('alertdialog')).toHaveCount(0);
      await expect(remove).toHaveCount(0);
      await expect(drawer).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(page.getByRole('dialog')).toHaveCount(0);
      const detail = await (await request.get(`/api/admin/products/${id}`, authed())).json();
      expect(detail.images).toHaveLength(0);
    } finally {
      await request.put(`/api/admin/products/${id}/status`, authed({ data: { status: 'Archived' } }));
    }
  });
});

test.describe('حسابات المنصّة وسجلّ النشاط', () => {
  test.skip(!API_LOG, 'SOUQ_API_LOG must point to the running API\'s log to follow the invitation link');

  /** @type {import('@playwright/test').Page} */
  let owner;
  const email = `qa-ops-${stamp}@souq.test`;
  // الرقم صريح: السياسة تشترط حرفاً ورقماً، و`toString(36)` يخرج أحياناً بحروفٍ فقط
  // (انظر platform-provisioning.spec.js).
  const password = `Qa-Ops-${stamp}-Pass1`;
  let opsId;
  let invitationLink;

  test.beforeAll(async ({ browser }) => {
    owner = await browser.newPage({ locale: 'en-US' });
    await owner.addInitScript(() => localStorage.setItem('souq_lang', 'en'));
    await signIn(owner, PLATFORM, OWNER);
    await owner.getByRole('link', { name: /^Accounts$/ }).waitFor({ timeout: 45_000 });
  });

  test.afterAll(async () => { await owner?.close(); });

  test('1 · the owner invites a platform administrator', async () => {
    const logBefore = readFileSync(API_LOG, 'utf8').length;
    await owner.getByRole('link', { name: /^Accounts$/ }).click();
    await owner.locator('table').waitFor({ timeout: 45_000 });
    expect(await axe(owner)).toEqual([]);

    await owner.getByRole('button', { name: '+ Invite account' }).click();
    const drawer = owner.getByRole('dialog');
    await expect(drawer.getByRole('radio', { name: /Platform administrator/ })).toBeChecked();
    expect(await axe(owner, '[role="dialog"]')).toEqual([]);
    await drawer.getByLabel('Full name').fill(`QA Ops ${stamp}`);
    await drawer.getByLabel('Email').fill(email);
    const invited = owner.waitForResponse((r) => r.url().endsWith('/api/platform/users') && r.request().method() === 'POST');
    await drawer.getByRole('button', { name: 'Send invitation' }).click();
    const response = await invited;
    expect(response.status()).toBe(200);
    opsId = (await response.json()).userId;

    const row = owner.locator('tr', { hasText: email });
    await expect(row.getByText('Invitation pending')).toBeVisible({ timeout: 15_000 });

    // الدعوة على مضيف المنصّة نفسه، من صندوق الصادر.
    const findLink = () => readFileSync(API_LOG, 'utf8').slice(logBefore).match(/https?:\/\/admin\.localhost:5173\/accept-invitation\S+/)?.[0];
    await expect.poll(findLink, { message: 'platform invitation link in the API log', timeout: 45_000, intervals: [1000] }).toBeTruthy();
    invitationLink = findLink();
  });

  test('2 · the administrator accepts, signs in, and cannot reach platform accounts', async ({ browser }) => {
    const context = await browser.newContext({ locale: 'en-US' });
    await context.addInitScript(() => localStorage.setItem('souq_lang', 'en'));
    const ops = await context.newPage();
    await ops.goto(invitationLink);
    await ops.getByLabel('New password').fill(password);
    await ops.getByLabel('Confirm password').fill(password);
    await ops.getByRole('button', { name: 'Activate account' }).click();
    await ops.waitForURL((url) => !url.pathname.includes('accept-invitation'), { timeout: 30_000 });
    if (!ops.url().includes('/platform')) await signIn(ops, PLATFORM, { email, password });

    await ops.getByRole('link', { name: /^Stores$/ }).waitFor({ timeout: 45_000 });
    await expect(ops.getByRole('link', { name: /^Activity log$/ })).toBeVisible();
    await expect(ops.getByRole('link', { name: /^Accounts$/ })).toHaveCount(0);
    await ops.goto(`${PLATFORM}/platform/accounts`);
    await ops.waitForURL((url) => !url.pathname.includes('/accounts'), { timeout: 30_000 });

    // 3 · the owner disables them behind a confirmation, and their session falls.
    await owner.reload();
    const row = owner.locator('tr', { hasText: email });
    await expect(row.getByText('Active')).toBeVisible({ timeout: 30_000 });
    let statusCalls = 0;
    owner.on('request', (r) => { if (r.url().includes(`/api/platform/users/${opsId}/status`)) statusCalls += 1; });

    await row.getByRole('button').last().click();
    await owner.getByRole('menuitem', { name: 'Disable' }).click();
    await expect(owner.getByRole('alertdialog')).toContainText(`QA Ops ${stamp}`);
    await owner.keyboard.press('Escape');
    await expect(owner.getByRole('alertdialog')).toHaveCount(0);
    expect(statusCalls).toBe(0);

    await row.getByRole('button').last().click();
    await owner.getByRole('menuitem', { name: 'Disable' }).click();
    await owner.getByRole('alertdialog').getByRole('button', { name: 'Disable account' }).click();
    await expect(owner.getByRole('alertdialog')).toHaveCount(0);
    await expect(row.getByText('Disabled')).toBeVisible({ timeout: 15_000 });
    expect(statusCalls).toBe(1);

    await ops.goto(`${PLATFORM}/platform/stores`);
    await ops.waitForURL((url) => url.pathname.includes('/login'), { timeout: 30_000 });
    await context.close();
  });

  test('4 · the disable is in the activity log: by activity, by account, paged by the server', async () => {
    await owner.getByRole('link', { name: /^Activity log$/ }).click();
    await owner.locator('table').waitFor({ timeout: 45_000 });
    expect(await axe(owner)).toEqual([]);

    const queries = [];
    owner.on('request', (r) => { if (r.url().includes('/api/platform/audit')) queries.push(new URL(r.url()).searchParams); });

    await owner.getByLabel('Activity', { exact: true }).selectOption('platform.user.disabled');
    expect(queries).toHaveLength(0);   // لا قراءة قبل التطبيق
    await owner.getByRole('button', { name: 'Apply filters' }).click();
    await owner.waitForURL(/action=platform\.user\.disabled/);
    const row = owner.locator('tr', { hasText: `User · ${opsId}` }).first();
    await expect(row).toBeVisible({ timeout: 15_000 });
    await expect(row.getByText('Platform account disabled')).toBeVisible();
    expect(queries.at(-1).get('action')).toBe('platform.user.disabled');

    await row.getByRole('button', { name: /Details of entry/ }).click();
    const drawer = owner.getByRole('dialog');
    await expect(drawer.getByText(/Platform owner/)).toBeVisible();
    await expect(drawer.getByText(/Z$/)).toBeVisible();
    expect(await axe(owner, '[role="dialog"]')).toEqual([]);
    await drawer.getByRole('button', { name: 'All activity by this account' }).click();
    await owner.waitForURL(/\?actorUserId=\d+$/);
    await owner.locator('tbody tr').first().waitFor();

    await owner.getByRole('button', { name: 'Clear filters' }).click();
    await owner.waitForURL((url) => url.search === '');
    const next = owner.getByRole('button', { name: /Next/ });
    if (await next.isEnabled()) {
      await next.click();
      await owner.waitForURL(/page=2/);
      await expect.poll(() => queries.at(-1)?.get('page')).toBe('2');
      expect(queries.at(-1).get('pageSize')).toBe('50');
    }
  });

  test('5 · a store page opens its own activity; Arabic and dark mode read correctly', async () => {
    await owner.getByRole('link', { name: /^Stores$/ }).click();
    const first = owner.locator('tbody a[href^="/platform/stores/"]').first();
    await first.waitFor({ timeout: 45_000 });
    const storeId = (await first.getAttribute('href')).split('/').pop();
    await first.click();
    // رابط الصفحة نفسها لا رابط التنقّل: يُنتظر داخل المحتوى بعد تحميل المتجر.
    await owner.getByRole('main').getByRole('link', { name: 'Activity log' }).click();
    await owner.waitForURL(new RegExp(`tenantId=${storeId}`));
    // كل سطر من هذا المتجر: ما يعرضه الجدول بعد وصول الصفحة من الخادم، لا هيكل التحميل قبلها.
    await expect(owner.getByText(/entr(y|ies) match/)).toBeVisible();
    const stores = await owner.locator('tbody tr td:nth-child(3)').allTextContents();
    expect(stores.length).toBeGreaterThan(0);
    expect([...new Set(stores)]).toEqual([`Store #${storeId}`]);

    await owner.getByRole('button', { name: 'Toggle language' }).click();
    await expect.poll(() => owner.evaluate(() => document.documentElement.dir)).toBe('rtl');
    await owner.getByRole('button', { name: 'التبديل بين الفاتح والداكن' }).click();
    await expect.poll(() => owner.evaluate(() => document.documentElement.dataset.theme)).toBe('dark');
    await expect(owner.getByRole('heading', { name: 'سجلّ النشاط' })).toBeVisible();
    await owner.locator('details').first().click();
    expect(await axe(owner)).toEqual([]);
    await owner.screenshot({ path: `${process.env.QA_SHOTS ?? 'test-results'}/audit-ar-dark.png`, fullPage: true });

    const detailsButton = owner.getByRole('button', { name: /تفاصيل السطر/ }).first();
    if (await detailsButton.count()) {
      await detailsButton.click();
      expect(await axe(owner, '[role="dialog"]')).toEqual([]);
      await owner.screenshot({ path: `${process.env.QA_SHOTS ?? 'test-results'}/audit-entry-ar-dark.png` });
      await owner.keyboard.press('Escape');
    }

    await owner.getByRole('link', { name: 'الحسابات' }).click();
    await owner.locator('table').waitFor();
    expect(await axe(owner)).toEqual([]);
    await owner.screenshot({ path: `${process.env.QA_SHOTS ?? 'test-results'}/accounts-ar-dark.png`, fullPage: true });

    await owner.getByRole('button', { name: 'التبديل بين الفاتح والداكن' }).click();
    await owner.getByRole('button', { name: 'تبديل اللغة' }).click();
    await expect.poll(() => owner.evaluate(() => document.documentElement.dir)).toBe('ltr');
  });
});
