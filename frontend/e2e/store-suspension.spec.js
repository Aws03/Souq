import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// تعليقُ متجرٍ من المنصّة، وما يعنيه ذلك فعلاً لكلٍّ من الزائر والتاجر (C3، قرار المالك
// `C-17` = B: **إيقافٌ للمتسوّق، لا للتاجر**).
//
// **ولماذا في متصفّح وقد غطّى اختبارُ التكامل القرار نفسه؟** لأنّ نصفَ هذا القرار لا يعيش في
// الخادم أصلاً. الخادم يفتح `/admin` و`/login` و`/track` على متجرٍ معلَّق ويغلق ما عداها؛ وما
// يفصل «قرارٌ صحيح» عن «منتجٌ صحيح» هو أن يرى الزائرُ سبباً مفهوماً بدل شاشةٍ بيضاء، وأن يجد
// التاجرُ طريقاً إلى لوحته من الصفحة التي تخبره أنّ متجره مغلق — لا أن يعرف مساراً بقلبه.
//
// وهي الفجوةُ التي تركتها الشريحةُ التي بنت C3: لا رحلةَ متصفّحٍ لمستها، واختبارُ التجهيز
// يتحقّق من غياب أزرار دورة الحياة لا من أثرها.
//
// **والمتجرُ يعود فعّالاً في `finally` مهما حدث**: هو المتجر التجريبي المشترك، وتركُه معلَّقاً
// يُسقط كلَّ رحلةٍ بعده ويقرأ عطبُها كأنّه عيبٌ في ميزةٍ أخرى.
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
let storeId;
let storeSlug;

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

// دورةُ الحياة تُنفَّذ من شاشة المتجر في لوحة المنصّة، بحوار تأكيدٍ كما يفعل مشغّل.
async function lifecycle(action) {
  await owner.goto(`${PLATFORM}/platform/stores/${storeId}`);
  const label = new RegExp(`^${action} store$`, 'i');
  await owner.getByRole('button', { name: label }).first().click();
  const dialog = owner.getByRole('alertdialog');
  await expect(dialog).toBeVisible();
  // POST لا PUT: نقطةُ دورة الحياة فعلٌ مُسمّى (`{ action }`) لا استبدالُ حقل حالة.
  const done = owner.waitForResponse((r) =>
    /\/api\/platform\/tenants\/\d+\/status$/.test(r.url()) && r.request().method() === 'POST');
  await dialog.getByRole('button', { name: label }).click();
  expect((await done).status()).toBeLessThan(400);
  await expect(dialog).toBeHidden();
}

test.beforeAll(async ({ browser }) => {
  owner = await browser.newPage({ locale: 'en-US' });
  merchant = await browser.newPage({ locale: 'en-US' });
  await signIn(owner, PLATFORM, OWNER);
  await signIn(merchant, BASE, ADMIN);

  storeSlug = await merchant.evaluate(async () => {
    const res = await fetch('/api/storefront/config');
    return (await res.json()).slug ?? null;
  });
  expect(storeSlug, 'تعذّر قراءة معرّف متجر التاجر النصّي').not.toBeNull();

  // ======================================================================
  // معرّفُ المتجر يُقرأ **من الشاشة** لا بنداء `fetch` في سياق الصفحة، ولا مفرّ من ذلك:
  // رمزُ الوصول يعيش في ذاكرة الوحدة لا في ملفّ تعريف ارتباط، فنداءٌ خامّ من الصفحة يخرج
  // بلا هوية ويُردّ 401 — وهو تصميمٌ مقصود (رمزٌ لا تحمله كلُّ طلبيةٍ تلقائياً). فالطريق
  // الصحيح هو طريق المشغّل: ابحث في قائمة المتاجر، وافتح المتجر، واقرأ معرّفه من العنوان.
  // ======================================================================
  await owner.goto(`${PLATFORM}/platform/stores?search=${encodeURIComponent(storeSlug)}`);
  await owner.getByRole('link', { name: new RegExp(storeSlug, 'i') }).first().click()
    .catch(async () => {
      // اسمُ المتجر قد لا يحوي معرّفه النصّي، فالرجوعُ إلى صفّ الجدول الذي يعرضه.
      await owner.locator('tr', { hasText: storeSlug }).first().getByRole('link').first().click();
    });
  await owner.waitForURL(/\/platform\/stores\/\d+/);
  storeId = Number(owner.url().match(/\/platform\/stores\/(\d+)/)[1]);
  expect(storeId, 'المنصّة لا ترى المتجر بمعرّفه النصّي').toBeGreaterThan(0);
});

test.afterAll(async () => {
  await owner?.close();
  await merchant?.close();
});

test('التعليق يُغلق واجهة التسوّق ويُبقي التاجر في لوحته، ثمّ التفعيل يعيد كلَّ شيء', async ({ browser }) => {
  const visitor = await browser.newPage({ locale: 'en-US' });
  try {
    // (أ) قبل التعليق: الزائر يتسوّق.
    await visitor.goto(BASE);
    await expect(visitor.getByText(/temporarily closed|مغلق مؤقتاً/)).toHaveCount(0);

    // (ب) التعليق من لوحة المنصّة، بتأكيدٍ كما يفعل مشغّل.
    await lifecycle('Suspend');

    // (ج) الزائر يرى **سبباً مفهوماً**، لا شاشةً بيضاء ولا خطأ شبكة — وهذا هو نصفُ `C-17`
    //     الذي لا يعيش في الخادم.
    await visitor.goto(BASE);
    await expect(visitor.getByRole('heading', { name: /temporarily closed|مغلق مؤقتاً/ })).toBeVisible();

    // ويجد طريقاً إلى الدخول: صاحبُ المتجر يصل إلى لوحته من هنا بلا أن يحفظ مساراً.
    await expect(visitor.getByRole('link', { name: /sign in|تسجيل الدخول|دخول/i })).toBeVisible();
    expect(await axe(visitor)).toEqual([]);

    // وصفحاتُ التسوّق كلُّها خلف البوّابة نفسها، لا الرئيسيةُ وحدها.
    await visitor.goto(`${BASE}/cart`);
    await expect(visitor.getByRole('heading', { name: /temporarily closed|مغلق مؤقتاً/ })).toBeVisible();

    // (د) **والتاجر يبقى في لوحته** — وهو جوهرُ الجواب B: إيقافٌ للمتسوّق لا للتاجر، فمَن
    //     يُطلَب منه إصلاحُ سبب الإيقاف يجب أن يستطيع الدخول ليُصلحه.
    await merchant.goto(`${BASE}/admin`);
    await expect(merchant.getByRole('link', { name: /^(Settings|الإعدادات)$/ })).toBeVisible({ timeout: 45_000 });
    await expect(merchant.getByRole('heading', { name: /temporarily closed|مغلق مؤقتاً/ })).toHaveCount(0);
  } finally {
    // (هـ) الإعادة إلى الحالة الفعّالة مهما حدث أعلاه.
    await lifecycle('Activate');
    await visitor.goto(BASE);
    await expect(visitor.getByRole('heading', { name: /temporarily closed|مغلق مؤقتاً/ })).toHaveCount(0);
    await visitor.close();
  }
});
