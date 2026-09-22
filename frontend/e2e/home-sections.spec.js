import { expect, test } from '@playwright/test';

// ============================================================================
// ترتيب أقسام الرئيسية، من المحرّر إلى ما يراه المتسوّق (C8، ADR-0060).
//
// **ولماذا في متصفّح وقد غطّى اختبارُ التكامل الحفظ والقراءة؟** لأنّ ما يشتريه التاجر بهذه
// الميزة ليس حقلاً في JSON بل **ترتيبَ ما يراه زبونُه**. واختبارُ التكامل يُثبت أنّ الخادم حفظ
// وأعاد؛ وهذه الرحلة تُثبت أنّ الصفحة **رُسمت بذلك الترتيب** بعد حفظٍ حقيقيّ وتحميلٍ كامل —
// وهي الحلقة التي انكسرت في هذا المشروع أكثر من مرّة: حقلٌ يُقبل بـ204 ولا يصل الشاشة.
//
// والترتيبُ يُقاس بمواضع العناصر في المستند لا بوجودها: قسمٌ «موجود» في مكانٍ خاطئ يمرّ من
// أيّ فحصِ وجود، وهو بالضبط العطب الذي تمنعه هذه الميزة.
//
// **والأقسام المُحرَّكة هنا هي «الكتالوج» و«وصل حديثاً» لا «العروض»**: بيانات العرض بلا منتجٍ
// مخفَّض، و`ProductSection` يُخفي نفسه حين لا نتائج — فمتجرٌ بلا تخفيضات لا يعرض صفّاً اسمه
// «عروض» فيه منتجاتٌ عادية. ذلك سلوكٌ صحيح، فلا يُقاس عليه ترتيب.
//
// الرحلةُ تُعيد الترتيب الافتراضي قبل أن تنتهي: المتجرُ التجريبيّ مشترك.
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

test.describe.configure({ mode: 'serial' });

/** @type {import('@playwright/test').Page} */
let page;

// مواضعُ الأقسام في المستند، بترتيب ظهورها. البانرُ والكتالوجُ لهما مرساةٌ ثابتة؛ والصفوفُ
// تُعرَف بعناوينها كما يقرؤها الزائر — فالفحصُ يقرأ الصفحة لا أسماءَ أصنافٍ داخلية.
const order = (target) => target.evaluate(() => {
  const marks = [
    ['hero', document.querySelector('h1')],
    ['catalog', document.querySelector('#catalog')],
  ];
  for (const heading of document.querySelectorAll('h2')) {
    const text = (heading.textContent || '').trim();
    if (/new arrivals|وصل حديثاً/i.test(text)) marks.push(['newArrivals', heading]);
    else if (/offers|العروض/i.test(text)) marks.push(['offers', heading]);
  }
  return marks
    .filter(([, el]) => el)
    .sort((a, b) => (a[1].compareDocumentPosition(b[1]) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1))
    .map(([name]) => name);
});

// مربّعُ القسم يُلتقط باسمه المنطوق: عنوانُه هو نصُّ `<label>` كاملاً، وهو نفسه ما يسمعه
// قارئُ الشاشة — فالفحصُ يقرأ ما يُقرأ للمستخدم لا مِحوراً للاختبار.
const sectionBox = (name) => page.getByRole('checkbox', { name: new RegExp(name) });

// أزرارُ التحريك تحمل اسمَ القسم **معزولاً باتّجاهه** (`{{name, bidi}}`): اسمٌ يكتبه أحدٌ قد
// يكون بعكس اتجاه الجملة، فيُحاط بمحرفَي عزلٍ من يونيكود. فالمطابقةُ بتعبيرٍ لا بنصٍّ حرفيّ —
// ووجودُ العازلَين هنا دليلٌ على أنّ القاعدة مطبَّقة، لا عقبةٌ في الاختبار.
const moveButton = (name, direction) =>
  page.getByRole('button', { name: new RegExp(`Move\\s*\\W?${name}\\W?\\s*${direction}`) });

// ============================================================================
// إعادةُ المتجر إلى الترتيب الافتراضي، **بإجراءٍ حتميّ لا بافتراضِ حالةٍ سابقة**: المتجر
// التجريبيّ مشترك، ورحلةٌ تفترض أنّها تبدأ من الافتراضي تفشل بعد أوّل رحلةٍ سبقتها — وتلك
// حالةٌ تُقرأ كعيبٍ في المنتج وليست منه.
//
// الإجراء: ادفع «العروض» لأسفل حتى يتعطّل زرُّها (صارت الأخيرة)، ثمّ أنزِل الكتالوج مرّةً
// فيتبادلان — الكتالوج أخيراً والعروضُ قبله، وهو موضعُهما في الافتراضي. وما لم يُحرَّك بقي
// على ترتيبه.
// ============================================================================
async function resetToDefaultOrder() {
  await page.goto('/admin/settings');
  for (const name of ['New arrivals', 'Full catalog']) {
    const box = sectionBox(`^${name}`);
    if (await box.isEnabled() && !(await box.isChecked())) await box.check();
  }

  // ادفع الكتالوج لأسفل حتى يتعطّل زرُّه: صار أخيراً، وهو موضعه في الافتراضي. وما لم يُحرَّك
  // بقي على ترتيبه، فالنتيجة هي الافتراضي كاملاً.
  for (let i = 0; i < 6; i += 1) {
    const down = moveButton('Full catalog', 'down');
    if (await down.isDisabled()) break;
    await down.click();
  }

  const save = page.getByRole('button', { name: /^(Save|Save changes|حفظ|حفظ التغييرات)$/ }).first();
  if (await save.isEnabled()) await saveSettings();
}

async function saveSettings() {
  const save = page.getByRole('button', { name: /^(Save|Save changes|حفظ|حفظ التغييرات)$/ }).first();
  await expect(save).toBeEnabled();
  const saved = page.waitForResponse((r) =>
    r.url().includes('/api/admin/store/settings') && r.request().method() === 'PUT');
  await save.click();
  expect((await saved).status()).toBeLessThan(300);
}

// تحميلٌ كامل: الترتيب يصل مع `/api/storefront/config`، وقياسُه بلا انتظاره يقيس الافتراضي.
async function storefrontOrder() {
  await page.goto('/');
  await expect(page.locator('#catalog')).toBeVisible();

  // **وتُنتظر صفوفُ الاكتشاف حتى تستقرّ**: `ProductSection` يرسم عنوانه أثناء التحميل ويُخفي
  // نفسه حين يعود فارغاً، فقياسٌ في منتصف الجلب يرى صفّاً لا يراه الزائر بعد ثانية. وهذا
  // ليس عطباً في المنتج بل في القياس — والفرقُ بينهما هو ما يجعل رحلةً فاشلة تُقرأ بحقّها.
  await page.waitForLoadState('networkidle');
  return order(page);
}

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage({ locale: 'en-US' });
  await page.goto('/login');
  await page.locator('input[type="email"]').first().fill(ADMIN.email);
  await page.locator('input[type="password"]').first().fill(ADMIN.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'));
  await resetToDefaultOrder();
});

test.afterAll(async () => {
  await page?.close();
});

test.describe('أقسام الرئيسية', () => {
  test('المحرّر يعرض الأقسام بترتيبها، والكتالوج لا يُطفأ', async () => {
    await page.goto('/admin/settings');

    await expect(page.getByText('Home page sections')).toBeVisible();

    // الكتالوجُ يُحرَّك ولا يُحذف — والمنعُ يُقرأ عند مربّع الاختيار لا بعد رفضِ الخادم.
    const catalog = sectionBox('Full catalog');
    await expect(catalog).toBeDisabled();
    // والسببُ منطوقٌ مع الاسم، لا لونٌ باهتٌ وحده.
    await expect(catalog).toHaveAccessibleName(/always shown/);

    // زرّا التحريك لهما اسمٌ منطوق، لا سهمٌ وحده.
    await expect(moveButton('Full catalog', 'up')).toBeVisible();
    await expect(moveButton('Banner', 'up')).toBeDisabled();
    await expect(moveButton('Full catalog', 'down')).toBeDisabled();
  });

  test('الترتيب المحفوظ هو ما يراه المتسوّق بعد تحميلٍ كامل', async () => {
    expect(await storefrontOrder()).toEqual(['hero', 'newArrivals', 'catalog']);

    await page.goto('/admin/settings');
    // الكتالوجُ إلى أعلى مرّتين: يسبق «وصل حديثاً» — تغييرٌ يراه المتسوّق فوراً.
    await moveButton('Full catalog', 'up').click();
    await moveButton('Full catalog', 'up').click();
    await saveSettings();

    expect(await storefrontOrder()).toEqual(['hero', 'catalog', 'newArrivals'],
      'الحفظ ثمّ التحميل الكامل: هذه هي الحلقة التي لا يراها اختبار وحدة');
  });

  test('القسم المُطفأ يغيب عن الواجهة فعلاً', async () => {
    await page.goto('/admin/settings');
    await sectionBox('^New arrivals').uncheck();
    await saveSettings();

    expect(await storefrontOrder()).toEqual(['hero', 'catalog']);
  });

  // اللغتان والاتجاهان: القائمةُ عموديّة فلا «أعلى/أسفل» يقلبه الاتجاه — وهذا ما يُفحص،
  // أنّ الترتيب المرئيّ هو نفسه في العربية، وأنّ المستند فعلاً بالاتجاه المقلوب.
  test('اللغتان: الاتجاه ينقلب والترتيب لا يتبعه', async () => {
    await page.goto('/');
    const before = await page.evaluate(() => document.documentElement.dir);

    await page.getByRole('button', { name: /toggle language|تبديل اللغة/i }).first().click();
    // الاتجاه ينقلب مع النصّ لا قبله: حزمة اللغة تُحمَّل عند الطلب.
    await page.waitForFunction((d) => document.documentElement.dir !== d, before, { timeout: 10_000 });

    const after = await page.evaluate(() => document.documentElement.dir);
    expect([before, after].sort()).toEqual(['ltr', 'rtl']);

    // **وهذا هو الفحص**: القائمةُ عموديّة، فالترتيبُ المرئيّ هو نفسه في الاتجاهين. قاعدةٌ
    // فيزيائية (`left`/`right`) في التخطيط كانت ستقلبه هنا وحدها.
    expect(await storefrontOrder()).toEqual(['hero', 'catalog']);

    // ولا تمرير أفقي في الاتجاه المقلوب — وهو أوّلُ ما تكشفه قاعدةٌ فيزيائية.
    const overflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);

    await page.getByRole('button', { name: /toggle language|تبديل اللغة/i }).first().click();
    await page.waitForFunction((d) => document.documentElement.dir !== d, after, { timeout: 10_000 });
  });

  test('على الهاتف: الترتيب نفسه وبلا تمرير أفقي', async () => {
    await page.setViewportSize({ width: 390, height: 844 });
    expect(await storefrontOrder()).toEqual(['hero', 'catalog']);

    const overflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);

    await page.setViewportSize({ width: 1280, height: 800 });
  });

  test('تُعاد الحالة إلى الافتراضي', async () => {
    await resetToDefaultOrder();

    expect(await storefrontOrder()).toEqual(['hero', 'newArrivals', 'catalog']);
  });
});
