import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// محرّك البحث المحلي (M3، ADR-0042) على مكدّس حقيقي.
//
// اختبارات الوحدة تثبت أنّ التطبيع والمسافة صحيحان، واختبارات التكامل تثبت أنّ القاعدة تطابقهما. هذا الملف يثبت
// الشيء الذي لا يثبته أيّ منهما: **أنّ متسوّقاً على متصفّح حقيقي يرى ذلك فعلاً** — الكتابة تُظهر قائمة، ولوحة
// المفاتيح تعمل، ولافتة التصحيح تظهر، ولا شيء يفيض أفقياً على هاتف.
//
// يُشغَّل على حزمة الحاويات أو على خادم التطوير:
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/search.spec.js --project=desktop
//
// الكتالوج المتوقَّع هو بيانات العرض المبذورة (DbSeeder.SeedCatalogAsync): أسماء عربية مشكَّلة جزئياً، وهي
// المادة التي يوجد التطبيع لأجلها — فالبحث هنا بما يكتبه الناس فعلاً، لا بما كتبه التاجر حرفياً.
// ============================================================================
const require = createRequire(import.meta.url);

test.describe.configure({ mode: 'serial' });

let page;
test.beforeAll(async ({ browser }) => {
  page = await browser.newPage();
  // العربية صراحةً: لغة الواجهة تأتي من localStorage ثم من المتصفّح، ولغة Playwright المضبوطة en-US تجعل
  // البطاقات تُعرض بالإنجليزية. ورحلة بحث عربية يجب أن تجري على المتجر العربي بـ dir=rtl — وهو موضع العطل
  // المحتمل الذي تبحث عنه هذه الرحلة أصلاً.
  await page.addInitScript(() => {
    try { localStorage.setItem('souq_lang', 'ar'); } catch { /* نافذة خاصة: تبقى لغة المتصفّح */ }
  });
});

test('0 · the Arabic storefront renders right-to-left', async () => {
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
});
test.afterEach(async ({}, testInfo) => {
  if (testInfo.status !== testInfo.expectedStatus) console.log(`\n  URL AT FAILURE: ${page.url()}\n`);
});
test.afterAll(async () => { await page?.close(); });

const searchBox = () => page.getByRole('search').first().getByRole('combobox');

// axe على الحالة المعروضة فعلاً (القائمة مفتوحة) لا على الصفحة الساكنة.
async function axe(target) {
  await page.addScriptTag({ path: require.resolve('axe-core/axe.min.js') });
  return page.evaluate(async (selector) => {
    const result = await window.axe.run(selector, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
      rules: { 'color-contrast': { enabled: false } },
    });
    return result.violations.map((v) => `${v.id}: ${v.nodes.length} node(s)`);
  }, target);
}

test('1 · unvocalized Arabic finds a vocalized catalog name', async () => {
  // "نظاره شمسيه" مقابل "نظّارة شمسية": شدّة وتاء مربوطة. قبل M3 لم يتطابقا إطلاقاً.
  await page.goto('/');
  await searchBox().fill('نظاره شمسيه');
  await searchBox().press('Enter');

  await expect(page).toHaveURL(/[?&]q=/);
  await expect(page.locator('article').first()).toBeVisible();
  await expect(page.locator('article').first()).toContainText('نظّارة');
});

test('2 · a typo recovers, and the storefront says which word it searched', async () => {
  // "مصباخ" خطأ بحرف واحد عن "مصباح" — والاسترجاع من مفردات الكتالوج نفسه بلا أي بيانات تصحيح مُدخلة.
  await page.goto('/?q=%D9%85%D8%B5%D8%A8%D8%A7%D8%AE');

  const banner = page.getByRole('status').filter({ hasText: 'مصباح' }).first();
  await expect(banner).toBeVisible();
  await expect(banner).toContainText('مصباخ', { useInnerText: true });
  await expect(page.locator('article').first()).toContainText('مصباح');
});

test('3 · insisting on the original words is honoured, not overridden', async () => {
  await page.goto('/?q=%D9%85%D8%B5%D8%A8%D8%A7%D8%AE');
  await page.getByRole('button', { name: /مصباخ/ }).first().click();

  // الرفض يظهر في الرابط (قابل للمشاركة) والنتيجة فارغة صراحةً — لا تصحيح مفروض.
  await expect(page).toHaveURL(/exact=1/);
  await expect(page.locator('article')).toHaveCount(0);
});

test('4 · suggestions open while typing and are keyboard-operable', async () => {
  await page.goto('/');
  await searchBox().fill('مصب');

  const listbox = page.getByRole('search').first().getByRole('listbox');
  await expect(listbox).toBeVisible();
  await expect(searchBox()).toHaveAttribute('aria-expanded', 'true');

  // سهم لأسفل يُعلِن العنصر النشط بـ aria-activedescendant والتركيز يبقى في الحقل.
  await searchBox().press('ArrowDown');
  const activeId = await searchBox().getAttribute('aria-activedescendant');
  expect(activeId).toBeTruthy();
  await expect(page.locator(`#${activeId}`)).toHaveAttribute('aria-selected', 'true');
  await expect(searchBox()).toBeFocused();

  // Enter يفتح المقترح — وجهة لا نصّ يُكتب في الحقل.
  await searchBox().press('Enter');
  await expect(page).toHaveURL(/\/products\//);
});

test('5 · Escape closes the list without navigating', async () => {
  await page.goto('/');
  await searchBox().fill('مصب');
  await expect(page.getByRole('search').first().getByRole('listbox')).toBeVisible();

  await searchBox().press('Escape');

  await expect(searchBox()).toHaveAttribute('aria-expanded', 'false');
  await expect(page).toHaveURL(/\/$|\/\?/);
});

test('6 · the open suggestion list is axe-clean', async () => {
  await page.goto('/');
  await searchBox().fill('مصب');
  await expect(page.getByRole('search').first().getByRole('listbox')).toBeVisible();

  expect(await axe('[role="search"]')).toEqual([]);
});

test('7 · the suggestion list fits a narrow phone with no horizontal overflow', async () => {
  // 320px هو أضيق ما يُتوقَّع عملياً. قائمة تفيض هنا تعني شريط تمرير أفقياً على الصفحة كلّها.
  await page.setViewportSize({ width: 320, height: 720 });
  await page.goto('/');

  // الصفحة نفسها لا تفيض أفقياً على 320px. يُقاس قبل فتح الورقة: الورقة تحجب تمرير body وتُغيّر القياس،
  // وشريط الإعلان المتحرّك (قائم قبل M3) يترك هامشاً صغيراً متغيّراً — فحراسة الصفحة تكون هنا، وحراسة ما
  // يملكه M3 (القائمة ومجموعة الترتيب) بعد الفتح.
  const pageOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(pageOverflow, 'الصفحة تفيض أفقياً على 320px').toBeLessThanOrEqual(0);

  // على عرض الهاتف صندوق سطح المكتب مخفيّ تماماً (display:none فلا وجود له في شجرة الوصول)، والبحث يعيش
  // في الورقة السفلية. تُفتح صراحةً ويُتحقَّق من فتحها: شرطٌ صامت هنا كان سيُخفي أنّ الصندوق غير موجود أصلاً.
  await expect(page.getByRole('search')).toHaveCount(0);
  await page.getByRole('button', { name: 'فتح القائمة' }).click();

  const sheet = page.getByRole('dialog');
  await expect(sheet).toBeVisible();
  const box = sheet.getByRole('combobox');
  await box.fill('مصب');
  const listbox = sheet.getByRole('listbox');
  await expect(listbox).toBeVisible();

  const measured = await page.evaluate(() => {
    const el = document.querySelector('[role="listbox"]:not([hidden])');
    const rect = el.getBoundingClientRect();
    const group = document.querySelector('[role="group"]');
    return {
      list: { start: Math.round(rect.left), end: Math.round(rect.right) },
      sortGroup: group ? Math.round(group.getBoundingClientRect().width) : null,
      viewport: window.innerWidth,
    };
  });

  expect(measured.list.start, 'القائمة تبدأ خارج النافذة').toBeGreaterThanOrEqual(0);
  expect(measured.list.end, 'القائمة تنتهي خارج النافذة').toBeLessThanOrEqual(measured.viewport);
  // مجموعة الترتيب كسبت زرّاً رابعاً في M3 ("الأكثر مطابقةً") فكانت تفيض 129px على 320px قبل أن تُلتَفّ.
  expect(measured.sortGroup, 'مجموعة الترتيب تفيض النافذة').toBeLessThanOrEqual(measured.viewport);

  await page.setViewportSize({ width: 1280, height: 720 });
});
