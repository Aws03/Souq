import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// إدارة المتجر من داخله على مكدّس حقيقي: إعداداته وفريقه.
//
// ما يُفحص هنا لا يُرى بلا متصفّح وخادم: أن الحفظ يصل إلى المتجر الذي يراه الزائر، وأن لوحة غير مقروءة لا
// تُرسل أصلاً، وأن المعاينة تشتقّ رموزها داخل إطارها دون أن تُلوّن اللوحة، وأن ملفاً متنكّراً يُرفض بصوت.
//
// المتجر التجريبي مشترك مع بقية الرحلات، فكل ما يُعدَّل فيه يُعاد كما كان في finally — رحلة تترك المتجر
// بإعلان غريب تكسر رحلة أخرى لا علاقة لها.
//
// دخول واحد للملف كلّه، وصفحة واحدة يُتنقَّل فيها بروابط اللوحة كما يفعل التاجر، لا بإعادة تحميل:
//   • الخادم يحدّ الدخول بعشر محاولات في الدقيقة (RateLimiting.Auth) — دخولٌ لكل رحلة يصطدم به.
//   • كل تحميل كامل يجدّد الجلسة ويدوّر رمزها؛ وتحميلٌ يقطع تجديداً جارياً يترك المتصفّح برمزٍ دوّره
//     الخادم فعلاً، فيُعامَل التحميل التالي سرقةً وتسقط الجلسة. رحلات بـ page.goto متلاحقة كانت تخرج
//     من الجلسة في منتصف الملف، بلا عيب في الشاشات.
// وما يراه الزائر يُفحص في سياق مستقلّ بلا دخول: الزائر لا يملك جلسة المدير أصلاً.
// ============================================================================
// بيانات المدير من البيئة بقيمها الافتراضية (M10): كانت مكتوبةً حرفيّاً، فلم يكن هذا الملفّ قابلاً
// للتشغيل إلا على حزمةٍ مبذورةٍ بهذين تحديداً — وعلى غيرها يفشل عند الدخول بمهلةٍ منتهية، لا برسالةٍ
// تقول السبب. (العلّة نفسها التي كانت في مضيف المنصّة داخل responsive.spec.js، صُحِّحت في M9.)
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};
const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');

test.describe.configure({ mode: 'serial' });

/** @type {import('@playwright/test').Page} */
let page;

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage({ locale: 'en-US' });
  await signIn(page);
  await page.goto('/admin');
  await page.getByRole('link', { name: /^(Settings|الإعدادات)$/ }).waitFor({ timeout: 45_000 });
});

test.afterAll(async () => { await page?.close(); });

async function signIn(page) {
  await page.goto('/login');
  await page.locator('input[type="email"]').first().fill(ADMIN.email);
  await page.locator('input[type="password"]').first().fill(ADMIN.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'));
}

const openSettings = async (page) => {
  await page.getByRole('link', { name: /^(Settings|الإعدادات)$/ }).click();
  await page.locator('#settings-colors-primary').waitFor({ timeout: 45_000 });
};

const openTeam = async (page) => {
  await page.getByRole('link', { name: /^(Team|الفريق)$/ }).click();
  await page.locator('table').waitFor({ timeout: 45_000 });
};

const saveSettings = async (page) => {
  const saved = page.waitForResponse((r) => r.url().endsWith('/api/admin/store/settings') && r.request().method() === 'PUT');
  await page.getByRole('button', { name: /^(Save|حفظ)$/ }).click();
  return saved;
};

test.describe('إعدادات المتجر', () => {
  test('ما يُحفظ يصل إلى واجهة المتجر كما يراها زائر', async ({ browser }) => {
    await openSettings(page);
    const field = page.locator('#settings-announcement-en');
    const original = await field.inputValue();
    const marker = `QA announcement ${Date.now()}`;

    try {
      await field.fill(marker);
      await expect(page.getByText(/Unsaved changes|تعديلات غير محفوظة/)).toBeVisible();
      expect((await saveSettings(page)).status()).toBe(204);
      await expect(page.getByText(/All changes saved|كل التعديلات محفوظة/)).toBeVisible();

      const visitor = await browser.newContext({ locale: 'en-US' });
      await visitor.addInitScript(() => localStorage.setItem('souq_lang', 'en'));
      const storefront = await visitor.newPage();
      await storefront.goto('/');
      // الشريط يكرّر نصّه ليتمرّر بلا فجوة — أيّ نسخة ظاهرة تكفي.
      await expect(storefront.getByText(marker).first()).toBeVisible({ timeout: 30_000 });
      await visitor.close();
    } finally {
      await openSettings(page);
      await page.locator('#settings-announcement-en').fill(original);
      if (await page.getByRole('button', { name: /^(Save|حفظ)$/ }).isEnabled()) {
        expect((await saveSettings(page)).status()).toBe(204);
      }
    }
  });

  test('لوحة غير مقروءة لا تُرسل إلى الخادم أصلاً', async () => {
    await openSettings(page);
    const puts = [];
    page.on('request', (r) => { if (r.method() === 'PUT' && r.url().includes('/admin/store/settings')) puts.push(r); });

    const text = page.locator('#settings-colors-text');
    await text.fill('#CCCCCC');
    await page.getByRole('button', { name: /^(Save|حفظ)$/ }).click();

    await expect(text).toHaveAttribute('aria-invalid', 'true');
    await expect(text).toBeFocused();
    await page.waitForTimeout(500);
    expect(puts).toHaveLength(0);

    await page.getByRole('button', { name: /Discard changes|تجاهل التعديلات/ }).click();
    await expect(page.getByText(/All changes saved|كل التعديلات محفوظة/)).toBeVisible();
  });

  test('المعاينة الداكنة تشتقّ رموزها داخل إطارها ولا تُلوّن اللوحة', async () => {
    await openSettings(page);
    const frame = page.locator('figure [data-theme]');
    const pageTheme = await page.evaluate(() => document.documentElement.dataset.theme);
    const tokens = () => frame.evaluate((el) => ({
      bg: getComputedStyle(el).getPropertyValue('--color-bg').trim(),
      text: getComputedStyle(el).getPropertyValue('--color-text').trim(),
    }));

    const light = await tokens();
    await page.getByRole('button', { name: /^(Dark|داكن)$/ }).click();
    await expect(frame).toHaveAttribute('data-theme', 'dark');
    const dark = await tokens();

    expect(dark.bg).not.toBe(light.bg);
    expect(dark.text).not.toBe(light.text);
    expect(await page.evaluate(() => document.documentElement.dataset.theme)).toBe(pageTheme);
  });

  test('ملف SVG متنكّر في شعار يُرفض برسالة، والشعار الحالي باقٍ', async () => {
    await openSettings(page);
    const upload = page.waitForResponse((r) => r.url().includes('/api/admin/store/branding/logo'));
    await page.locator('input[type="file"]').first().setInputFiles({
      name: 'logo.png', mimeType: 'image/png',
      buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>'),
    });

    expect((await upload).status()).toBe(400);
    await expect(page.getByRole('alert').filter({ hasText: /not supported|PNG|غير/i })).toBeVisible();
  });

  test('لا مخالفات إتاحة على الشاشة كاملة', async () => {
    await openSettings(page);
    await page.addScriptTag({ content: axeSource });
    const violations = await page.evaluate(async () =>
      // @ts-ignore — axe يُحقن في الصفحة
      (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations
        .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`));
    expect(violations).toEqual([]);
  });
});

test.describe('فريق المتجر', () => {
  test('دعوة عضو ثم إيقافه: الحالة تتبع الخادم', async () => {
    const email = `qa-staff-${Date.now()}@souq.test`;
    await openTeam(page);
    await page.getByRole('button', { name: /Invite member|دعوة عضو/ }).click();

    const dialog = page.getByRole('dialog');
    await dialog.getByLabel(/Full name|الاسم الكامل/).fill('QA Staff');
    await dialog.getByLabel(/^(Email|البريد الإلكتروني)$/).fill(email);
    const invited = page.waitForResponse((r) => r.url().endsWith('/api/admin/staff') && r.request().method() === 'POST');
    await dialog.getByRole('button', { name: /Send invitation|إرسال الدعوة/ }).click();
    expect((await invited).status()).toBe(200);
    await expect(dialog).toBeHidden();

    const row = page.locator('tr', { hasText: email });
    await expect(row.getByText(/Invitation pending|بانتظار قبول الدعوة/)).toBeVisible();

    // الإيقاف يُؤكَّد في حوار اللوحة (لا window.confirm): لا طلب قبل زرّ التأكيد.
    await row.getByRole('button').click();
    await page.getByRole('menuitem', { name: /^(Disable|إيقاف)$/ }).click();
    const confirm = page.getByRole('alertdialog');
    await expect(confirm).toContainText('QA Staff');
    await confirm.getByRole('button', { name: /^(Disable account|إيقاف الحساب)$/ }).click();
    await expect(confirm).toBeHidden();
    await expect(row.getByText(/^(Disabled|موقوف)$/)).toBeVisible();
  });

  test('المدير لا يُعرض عليه إيقاف نفسه', async () => {
    await openTeam(page);
    const own = page.locator('tr', { hasText: ADMIN.email });
    await expect(own).toBeVisible({ timeout: 30_000 });
    await expect(own.getByRole('button')).toHaveCount(0);
  });

  test('لا مخالفات إتاحة', async () => {
    await openTeam(page);
    await page.addScriptTag({ content: axeSource });
    const violations = await page.evaluate(async () =>
      // @ts-ignore — axe يُحقن في الصفحة
      (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations
        .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`));
    expect(violations).toEqual([]);
  });
});
