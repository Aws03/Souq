import { expect, test } from '@playwright/test';

// ============================================================================
// القوالبُ الثلاثة، في متصفّح (C8، TD-65، ADR-0059).
//
// **ولماذا هنا تحديداً وقد فحصتها الوحدة؟** لأنّ العيب الذي تُغلقه هذه المرحلة لم يكن في قيمةٍ
// خاطئة بل في **قيمةٍ لا تصل**: السمةُ كانت تُكتب على `<html>` منذ المرحلة 15، والرموزُ كانت
// معرَّفةً، ولا أحد يختار عليها. واختبارُ وحدةٍ يقرأ الورقة لا يستطيع أن يرى ذلك — يراه متصفّحٌ
// يقيس `getComputedStyle` بعد أن يمرّ كلُّ شيء: الحفظ، وجولةُ الخادم، والكتابةُ السطرية.
//
// والفحصُ يقرأ الرمز لا شكل عنصرٍ بعينه: بطاقةٌ قد تُعاد تسميتها غداً، و`--shadow` هو **العقد**
// الذي يقرؤه كلُّ سطحٍ في المنتج. فلو صار حلقةً فقد صار حلقةً في كلّ مكان.
//
// الرحلةُ تُعيد المتجرَ إلى `classic` قبل أن تنتهي: المتجرُ التجريبيّ مشترك، وتركُه على قالبٍ
// آخر يجعل رحلةً لاحقة تقيس شاشةً لم تتوقّعها.
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

test.describe.configure({ mode: 'serial' });

/** @type {import('@playwright/test').Page} */
let page;

// قيمةُ خاصّيةٍ مخصّصة تُعاد **كما كُتبت** لا كما يُطبّعها المتصفّح لـ `box-shadow`: أي
// `0 0 0 1px #DFDED8` لا `0px 0px 0px 1px …`. و`var(--color-border)` تُستبدَل عند الحساب، وهو
// ما يُثبت أنّ الحلقة تقرأ رمز الحدّ فعلاً فتتبع الوضعَ الداكن.
const RING = /^0(px)? 0(px)? 0(px)? \dpx \S/;

// الرموزُ تُقرأ من الجذر بعد أن يستقرّ كلُّ شيء: هي ما تقرؤه المكوّنات فعلاً.
const rootTokens = (target) => target.evaluate(() => {
  const style = getComputedStyle(document.documentElement);
  return {
    preset: document.documentElement.dataset.preset,
    shadow: style.getPropertyValue('--shadow').trim(),
    radius: style.getPropertyValue('--radius').trim(),
    weight: style.getPropertyValue('--weight-display').trim(),
  };
});

// **والحفظُ يُتخطّى إن لم يتغيّر شيء، عمداً**: المحرّرُ يُعطّل «حفظ» على نموذجٍ نظيف — وهو
// سلوكٌ صحيح لا عطب. فحارسُ الرحلة أن القيمةَ المطلوبة هي المحفوظة في النهاية، لا أنّ طلباً وقع.
async function choosePreset(value) {
  await page.goto('/admin/settings');
  const select = page.getByLabel(/Theme preset|القالب/).first();
  await expect(select).toBeVisible();
  if (await select.inputValue() === value) return;

  await select.selectOption(value);
  const save = page.getByRole('button', { name: /^(Save|Save changes|حفظ|حفظ التغييرات)$/ }).first();
  await expect(save).toBeEnabled();

  const saved = page.waitForResponse((r) =>
    r.url().includes('/api/admin/store/settings') && r.request().method() === 'PUT');
  await save.click();
  expect((await saved).status()).toBeLessThan(300);
}

// تحميلٌ كامل للواجهة: الرموزُ تُكتب في `applyStoreTheme` بعد وصول `/api/storefront/config`،
// فقياسُها بلا انتظار ذلك يقيس نافذةَ ما-قبل-الإعداد لا القالبَ المحفوظ.
async function storefrontTokens() {
  await page.goto('/');
  await expect.poll(async () => (await rootTokens(page)).preset).toBeTruthy();
  return rootTokens(page);
}

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage({ locale: 'en-US' });
  await page.goto('/login');
  await page.locator('input[type="email"]').first().fill(ADMIN.email);
  await page.locator('input[type="password"]').first().fill(ADMIN.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'));
});

test.afterAll(async () => {
  await page?.close();
});

test.describe('قوالب المتجر', () => {
  let classic;

  test('الكلاسيكيُّ يطفو: ظلٌّ ناعم لا حلقة', async () => {
    await choosePreset('classic');
    classic = await storefrontTokens();

    expect(classic.preset).toBe('classic');
    expect(classic.shadow).not.toMatch(RING);
    expect(classic.shadow.length).toBeGreaterThan(0);
  });

  // العطبُ الذي تُغلقه المرحلة: الخيارُ كان يُحفظ ولا يُغيّر شيئاً. فالفحصُ مقارنةٌ لا قيمة.
  test('البسيطُ يستبدل بالظلّ خطّاً، ويضيّق القطر', async () => {
    await choosePreset('minimal');
    const minimal = await storefrontTokens();

    expect(minimal.preset).toBe('minimal');
    expect(minimal.shadow).toMatch(RING);
    expect(Number.parseInt(minimal.radius, 10)).toBeLessThan(Number.parseInt(classic.radius, 10));
    expect(Number(minimal.weight)).toBeLessThan(Number(classic.weight));
  });

  test('الجريءُ يُحدّ الزوايا ويُثقل الخطّ', async () => {
    await choosePreset('bold');
    const bold = await storefrontTokens();

    expect(bold.preset).toBe('bold');
    expect(bold.shadow).toMatch(RING);
    expect(Number.parseInt(bold.radius, 10)).toBeLessThan(Number.parseInt(classic.radius, 10));
    expect(Number(bold.weight)).toBeGreaterThan(Number(classic.weight));
  });

  // المعاينةُ هي الموضعُ الذي يقارن فيه التاجرُ الثلاثةَ **قبل** أن يحفظ. ومحدِّدٌ مربوطٌ
  // بـ `html` كان سيتركها بلا فرق — وهو العطب نفسه في مكانٍ آخر.
  test('معاينةُ الإعدادات تُظهر الفرق قبل الحفظ', async () => {
    await page.goto('/admin/settings');
    // `div` لا `[data-preset]` وحدها: الجذرُ يحملها أيضاً، والمقصودُ إطارُ المعاينة.
    const frame = page.locator('div[data-preset]').first();
    await expect(frame).toBeVisible();

    await page.getByLabel(/Theme preset|القالب/).first().selectOption('minimal');
    await expect(frame).toHaveAttribute('data-preset', 'minimal');
    const ring = await frame.evaluate((el) => getComputedStyle(el).getPropertyValue('--shadow').trim());
    expect(ring).toMatch(RING);

    // وتُعاد الحالةُ إلى الكلاسيكيّ، فالمتجرُ التجريبيّ مشترك.
    await choosePreset('classic');
    expect((await storefrontTokens()).preset).toBe('classic');
  });
});
