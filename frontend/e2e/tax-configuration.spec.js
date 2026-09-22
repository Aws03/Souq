import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// إعدادُ الضريبة من طرفيه على مكدّس حقيقي ([ADR-0055](0055)، قرار المالك P-06):
//   المنصّة تُنشئ اختصاصاً وتُدخل قواعده وتنشرها → **ولا يُجمَع شيءٌ بعد** → تُسجَّل شهادةُ
//   مهنيّ باسمه → التاجر يختار الاختصاص ويفعّل الجمع → يقرأ نسبتَه وحالةَ تحقّقها.
//
// **والخطوةُ الثالثة هي الرحلةُ كلُّها.** بين «منشور» و«يُجمَع به» فعلٌ بشريّ يُسمّي فاعله، وهو
// ما يمنع رقماً وجدته الهندسة في وثيقةٍ من أن يصل مشترياً حقيقياً. فالرحلةُ تقف عند تلك النقطة
// وتتحقّق من الصفر **قبل** الشهادة، لا بعدها فقط.
//
// والقيمُ هنا **صناعيّة للاختبار**: رمزُ اختصاصٍ عشوائيّ ونسبةٌ اختراعُها لا يضرّ أحداً لأنّها
// لا تخصّ بلداً. ولا يُنشئ هذا الملفّ اختصاصاً حقيقياً ولا يُسجّل له شهادةً حقيقية.
// ============================================================================
const BASE = process.env.SOUQ_E2E_BASE_URL || 'http://localhost:5173';
const platformOrigin = (base) => {
  const url = new URL(base);
  if (!url.hostname.startsWith('admin.')) url.hostname = `admin.${url.hostname}`;
  return url.origin;
};
const PLATFORM = platformOrigin(BASE);

const OWNER = {
  email: process.env.SOUQ_E2E_OWNER_EMAIL || 'owner@souq.com',
  password: process.env.SOUQ_E2E_OWNER_PASSWORD || 'Owner@12345',
};
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');

test.describe.configure({ mode: 'serial' });

/** @type {import('@playwright/test').Page} */
let owner;
/** @type {import('@playwright/test').Page} */
let merchant;

// رمزٌ فريد لكل تشغيل: الاختصاصُ فريدٌ في القاعدة، وإعادةُ التشغيل على الرمز نفسه تُرفض بحقّ.
const stamp = Date.now().toString(36).slice(-4).toUpperCase();
const JURISDICTION = `Q${stamp}`;

async function signIn(page, origin, account) {
  await page.goto(`${origin}/login`);
  await page.locator('input[type="email"]').first().fill(account.email);
  await page.locator('input[type="password"]').first().fill(account.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'));
}

const settleAnimations = (page) => page.evaluate(() => Promise.all(document.getAnimations()
  .filter((a) => a.effect?.getComputedTiming().iterations !== Infinity)
  .map((a) => a.finished.catch(() => {}))));

const axe = async (page) => {
  await settleAnimations(page);
  await settleAnimations(page);
  await page.addScriptTag({ content: axeSource });
  return page.evaluate(async () =>
    // @ts-ignore — axe يُحقن في الصفحة
    (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations
      .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`));
};

// لوحُ الاختصاص الذي أنشأته هذه الرحلة، بإصداراته مفتوحة.
const profilePanel = () => owner.locator('section', { hasText: JURISDICTION }).first();

test.beforeAll(async ({ browser }) => {
  owner = await browser.newPage({ locale: 'en-US' });
  merchant = await browser.newPage({ locale: 'en-US' });
  await signIn(owner, PLATFORM, OWNER);
  await signIn(merchant, BASE, ADMIN);
});

test.afterAll(async () => {
  // التاجر يعود بلا اختصاص: المتجر التجريبي مشترك، وتركُه يجمع ضريبةً اختبارية يُغيّر إجماليات
  // رحلاتٍ أخرى فتُقرأ أعطالُها كأنّها عيوبٌ في الدفع أو السلّة.
  try {
    await merchant.goto(`${BASE}/admin/tax`);
    await merchant.getByLabel('Jurisdiction', { exact: true }).selectOption('');
    await merchant.getByRole('button', { name: /^(Save|حفظ)$/ }).click();
    await merchant.getByText(/Not charging tax|لا يجمع ضريبة/).waitFor({ timeout: 15_000 });
  } catch {
    // لا تُخفِ فشلَ الرحلة بفشل التنظيف.
  }
  await owner?.close();
  await merchant?.close();
});

test('المنصّة تُنشئ اختصاصاً وتُدخل قواعده وتنشرها', async () => {
  await owner.goto(`${PLATFORM}/platform/tax`);

  // القاعدةُ التي تحكم الشاشة كلَّها، مكتوبةٌ حيث تُقرأ.
  await expect(owner.getByText(/configuration, not law|إعدادٌ لا قانون/)).toBeVisible();

  await owner.getByLabel(/Jurisdiction code|رمز الاختصاص/).fill(JURISDICTION);
  await owner.getByLabel(/^(Name|الاسم)$/).fill(`QA jurisdiction ${stamp}`);
  const created = owner.waitForResponse((r) =>
    r.url().endsWith('/api/platform/tax/profiles') && r.request().method() === 'POST');
  await owner.getByRole('button', { name: /Create jurisdiction|أنشئ الاختصاص/ }).click();
  expect((await created).status()).toBe(201);

  await expect(profilePanel()).toBeVisible();
  // بلا إصدارٍ بعد ⇒ لا يجمع شيئاً، ويُقال ذلك.
  await expect(profilePanel().getByText(/Charges nothing|لا يجمع شيئاً/)).toBeVisible();

  await profilePanel().getByRole('button', { name: /Show versions|أظهر الإصدارات/ }).click();

  // عُرفُ السعر وضريبةُ الشحن **سؤالان بلا افتراض**: الإرسالُ بلا إجابتهما مرفوض.
  await profilePanel().getByLabel(/Price convention|عُرف السعر/).selectOption('Exclusive');
  await profilePanel().getByLabel(/Is shipping taxed|هل يُضرَّب الشحن/).selectOption('false');

  await profilePanel().getByLabel(/Rate code|رمز النسبة/).fill('standard');
  await profilePanel().getByLabel(/Rate name|اسم النسبة/).fill('QA standard rate');
  await profilePanel().getByLabel(/Rate \(%\)|النسبة \(%\)/).fill('10');
  // النسبةُ تُدخَل بالمئة وتُحفَظ بنقاط الأساس — والمعاينةُ تقول ذلك قبل الحفظ.
  await expect(profilePanel().getByText(/1000 basis points|1000 نقطة أساس/)).toBeVisible();
  await profilePanel().getByRole('button', { name: /Add rate|أضف النسبة/ }).click();

  const drafted = owner.waitForResponse((r) => /\/versions$/.test(r.url()) && r.request().method() === 'POST');
  await profilePanel().getByRole('button', { name: /Create draft version|أنشئ مسوّدة الإصدار/ }).click();
  expect((await drafted).status()).toBe(201);

  // اللوحُ يبقى مفتوحاً بعد إنشاء المسوّدة، فلا يُضغط «أظهر الإصدارات» ثانيةً — واسمُ الزرّ
  // صار «أخفِ الإصدارات» أصلاً. ونقرةٌ على اسمٍ لا يطابق شيئاً تنتظر حتى تنفد مهلةُ **الاختبار**
  // لا مهلةُ المُحدِّد، فـ`.catch()` بعدها لا تقع أبداً — وهي مهلةٌ كاملة تُحرق بلا سبب.
  const publish = profilePanel().getByRole('button', { name: /Publish version|انشر الإصدار/ });
  await publish.click();
  const dialog = owner.getByRole('alertdialog');
  await expect(dialog).toContainText(/freezes every value|يُجمّد كلَّ قيمةٍ/);
  const published = owner.waitForResponse((r) => /\/publish$/.test(r.url()) && r.request().method() === 'POST');
  await dialog.getByRole('button', { name: /Publish version|انشر الإصدار/ }).click();
  expect((await published).status()).toBe(204);

  // **وهذه هي النقطة**: منشورٌ، ونافذ، وصحيحُ الشكل — ولا يجمع شيئاً بعد.
  await expect(profilePanel().getByText(/Charges nothing|لا يجمع شيئاً/)).toBeVisible();
  await expect(profilePanel().getByText(/Not verified|غير متحقَّق منه/).first()).toBeVisible();
  expect(await axe(owner)).toEqual([]);
});

test('لا يجمع التاجر شيئاً بإصدارٍ لم يؤكّده أحد، ويُقال له السبب بالاسم', async () => {
  await merchant.goto(`${BASE}/admin/tax`);

  // بالقيمة لا بالاسم: `selectOption` لا تقبل تعبيراً نمطياً في `label` (المزلق نفسه الذي
  // وقع في رحلة الفوترة)، والمعرّف يُقرأ من الخيار الذي يحمل رمزَ الاختصاص.
  const jurisdictionSelect = merchant.getByLabel('Jurisdiction', { exact: true });
  const option = jurisdictionSelect.locator(`option:text-matches("^${JURISDICTION} ")`);
  await expect(option).toHaveCount(1, { timeout: 15_000 });
  await jurisdictionSelect.selectOption(await option.getAttribute('value'));
  await merchant.getByLabel(/Charge tax on orders|اجمع الضريبة على الطلبات/).check();
  const saved = merchant.waitForResponse((r) =>
    r.url().endsWith('/api/admin/store/tax') && r.request().method() === 'PUT');
  await merchant.getByRole('button', { name: /^(Save|حفظ)$/ }).click();
  expect((await saved).status()).toBe(200);

  // اختار وفعّل — ولا يُجمَع، والسببُ مكتوبٌ بالاسم لا صفرٌ صامت.
  await expect(merchant.getByText(/Not charging tax|لا يجمع ضريبة/)).toBeVisible();
  await expect(merchant.getByText(/has not been verified|لم يتحقّق منه مهنيٌّ بعد/)).toBeVisible();
  expect(await axe(merchant)).toEqual([]);
});

test('شهادةُ مهنيٍّ باسمه هي وحدها ما يسمح بالجمع', async () => {
  await owner.goto(`${PLATFORM}/platform/tax`);
  await profilePanel().getByRole('button', { name: /Show versions|أظهر الإصدارات/ }).click();

  await profilePanel().getByRole('button', { name: /Record verification|سجّل التحقّق/ }).click();
  // ليست إقراراً من النظام بصحّة الأرقام، بل تسجيلاً لمن أقرّها — والاسمُ مطلوب.
  await profilePanel().getByLabel(/Verified by|تحقّق منه/).fill(`QA accountant ${stamp}`);
  const verified = owner.waitForResponse((r) => /\/verify$/.test(r.url()) && r.request().method() === 'POST');
  await profilePanel().getByRole('button', { name: /Record verification|سجّل التحقّق/ }).last().click();
  expect((await verified).status()).toBe(204);

  await expect(profilePanel().getByText(/^(Charges tax|يجمع الضريبة)$/)).toBeVisible();

  // وعند التاجر يصير الجمع فوراً، بنسبته وحالة تحقّقها ظاهرتين معاً.
  await merchant.goto(`${BASE}/admin/tax`);
  await expect(merchant.getByText(/^(Charging tax|يجمع الضريبة)$/)).toBeVisible();
  await expect(merchant.getByText('10%')).toBeVisible();
  await expect(merchant.getByText(/Verified by a professional|تحقّق منه مهنيّ/)).toBeVisible();
  expect(await axe(merchant)).toEqual([]);
});

test('سحبُ الشهادة يُوقف الجمع فوراً', async () => {
  await owner.goto(`${PLATFORM}/platform/tax`);
  await profilePanel().getByRole('button', { name: /Show versions|أظهر الإصدارات/ }).click();

  await profilePanel().getByRole('button', { name: /Withdraw verification|اسحب التحقّق/ }).click();
  const dialog = owner.getByRole('alertdialog');
  await expect(dialog).toContainText(/stops at once|يتوقّف الجمع/);
  const withdrawn = owner.waitForResponse((r) =>
    /\/require-confirmation$/.test(r.url()) && r.request().method() === 'POST');
  await dialog.getByRole('button', { name: /Withdraw verification|اسحب التحقّق/ }).click();
  expect((await withdrawn).status()).toBe(204);

  await merchant.goto(`${BASE}/admin/tax`);
  await expect(merchant.getByText(/Not charging tax|لا يجمع ضريبة/)).toBeVisible();
});
