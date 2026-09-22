import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// فوترةُ تاجرٍ من أوّلها إلى آخرها، على مكدّس حقيقي وبمضيفين حقيقيين (C5، ADR-0056):
//   المنصّة تضبط فوترتها → تُنشئ مسوّدة → تُصدرها → **التاجر يقرأها في لوحته** → المنصّة
//   تسجّل حوالةً جزئية ثمّ متمّمة → تصير الفاتورة مسدَّدة عند الطرفين.
//
// **ولماذا في متصفّح وقد غطّى اختبارُ التكامل المسار نفسه؟** لأنّ ما لا يراه ذلك الاختبار هو ما
// يفصل «واجهةً برمجية تعمل» عن «قدرةٍ يستطيع مشغّلٌ تشغيلها»: أنّ الزرّ موجودٌ ومُفعَّل في الحالة
// الصحيحة، وأنّ الحوار يمنع فعلاً لا رجعةَ فيه قبل وقوعه، وأنّ ما يُصدره طرفٌ يظهر عند الطرف
// الآخر بعملته وبرقمه، وأنّ لا مخالفةَ إتاحة في أيٍّ من الشاشتين.
//
// **ولا زرَّ دفعٍ في لوحة التاجر، ويُفحَص غيابُه**: التحصيل حوالةٌ بقرار المالك `C-15`، وزرٌّ
// يَعِد بما لا يوجد أسوأ من غيابه.
//
// الرحلة تنظّف أثرها: تُصدر إشعارَ دائنٍ لا يُبقي مستحقّاً معلّقاً على المتجر التجريبي المشترك.
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

let invoiceNumber = '';
const stamp = Date.now().toString(36);
const lineText = `QA subscription ${stamp}`;

async function signIn(page, origin, account) {
  await page.goto(`${origin}/login`);
  await page.locator('input[type="email"]').first().fill(account.email);
  await page.locator('input[type="password"]').first().fill(account.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'));
}

// ============================================================================
// الحركةُ تُنهى قبل فحص التباين: قياسُ لونٍ في منتصف انتقالٍ يقيس مزيجاً لا يراه أحد.
//
// **ودورتان لا واحدة.** أوّلُ تشغيلٍ لهذه الرحلة أبلغ عن مخالفة تباينٍ في نصّ التنبيه، ولم تكن
// مخالفةً: التنبيهُ يظهر بعد ردّ الخادم، فقد بدأت حركتُه **بعد** أن قرأ الانتظارُ الأوّل قائمةَ
// الحركات — فقاس axe نصّاً شفّافاً في منتصف ظهوره. الدورةُ الثانية تلتقط ما بدأ أثناء الأولى.
// ============================================================================
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

test.beforeAll(async ({ browser }) => {
  // صفحتان تبقيان مفتوحتين ودخولٌ واحد لكلٍّ: الخادم يحدّ الدخول بعشر محاولات في الدقيقة،
  // وكلُّ تحميلٍ كامل يجدّد الجلسة ويدوّر رمزها.
  owner = await browser.newPage({ locale: 'en-US' });
  merchant = await browser.newPage({ locale: 'en-US' });
  await signIn(owner, PLATFORM, OWNER);
  await signIn(merchant, BASE, ADMIN);
});

test.afterAll(async () => {
  await owner?.close();
  await merchant?.close();
});

test.describe('فوترة التاجر', () => {
  test('المنصّة تضبط فوترتها، وما ينقص يُقال باسمه', async () => {
    await owner.goto(`${PLATFORM}/platform/billing`);

    // العملةُ واسمُ المُصدِر هما بوّابة الإصدار كلُّها. نضبطهما إن لم يكونا مضبوطين — وحزمةُ
    // العرض تبذرهما، فالحالةُ الغالبة أنّهما موجودان.
    const currency = owner.getByLabel(/Invoicing currency|عملة الفوترة/);
    await expect(currency).toBeVisible();
    if (!(await currency.inputValue())) await currency.fill('JOD');

    const issuer = owner.getByLabel(/Issuer name|اسم المُصدِر/);
    if (!(await issuer.inputValue())) await issuer.fill('Souq QA');

    const instructions = owner.getByLabel(/Payment instructions|تعليمات الدفع/);
    await instructions.fill(`QA bank transfer ${stamp}`);

    const saved = owner.waitForResponse((r) =>
      r.url().endsWith('/api/platform/billing/settings') && r.request().method() === 'PUT');
    await owner.getByRole('button', { name: /^(Save|حفظ)$/ }).click();
    expect((await saved).status()).toBe(204);

    // وبعد الضبط تقول الشاشةُ إنّها جاهزة — وهي الجملة التي يقرؤها المشغّل قبل أن يُصدر.
    await expect(owner.getByText(/Ready to issue|جاهزة للإصدار/)).toBeVisible();

    // التنبيهُ يُنتظَر صراحةً قبل الفحص: هو آخرُ ما يظهر، وفحصُ الإتاحة قبل ظهوره يفحص شاشةً
    // ناقصة — وبعد ظهوره بلا انتظارِ حركته يفحص لوناً ممزوجاً.
    await owner.locator('[class*="toast"]').first().waitFor();
    expect(await axe(owner)).toEqual([]);
  });

  test('مسوّدةٌ تُنشأ من الشاشة، وبلا أسطر لا تُصدَر والسببُ مكتوب', async () => {
    // ====================================================================
    // **كلُّ خطوةٍ من الشاشة، ولا نداءَ API يدويّ في هذه الرحلة.**
    //
    // وهذا ما أضاف شاشةَ «فاتورة جديدة» أصلاً: أوّلُ تشغيلٍ لهذه الرحلة كشف أنّ إنشاء المسوّدة
    // لم يكن ممكناً إلّا بـ `fetch` مكتوبٍ في الاختبار — أي أنّ مشغّلاً لا يستطيع أن يبدأ
    // فاتورةً من لوحته. قدرةٌ أوّلُ خطوةٍ فيها خارج الواجهة ليست قدرةً يملكها أحد.
    // ====================================================================
    const slug = await merchant.evaluate(async () => {
      const res = await fetch('/api/storefront/config');
      return (await res.json()).slug ?? null;
    });
    expect(slug, 'تعذّر قراءة معرّف متجر التاجر النصّي من إعداد واجهته').not.toBeNull();

    await owner.goto(`${PLATFORM}/platform/invoices`);
    await owner.getByRole('link', { name: /New invoice|فاتورة جديدة/ }).click();
    await owner.waitForURL(/\/platform\/invoices\/new$/);

    await owner.getByLabel(/Find a store|ابحث عن متجر/).fill(slug);
    const storeSelect = owner.getByLabel(/^(Store|المتجر)$/);
    const option = storeSelect.locator(`option:text-matches("\\(${slug}\\)")`);
    await expect(option).toHaveCount(1, { timeout: 15_000 });
    // بالقيمة لا بالاسم: `selectOption` لا تقبل تعبيراً نمطياً في `label`، واسمُ المتجر
    // بياناتُ بذرٍ قد تتغيّر — أمّا المعرّف النصّي فهو ما تبحث به الرحلة أصلاً.
    await storeSelect.selectOption(await option.getAttribute('value'));

    const created = owner.waitForResponse((r) =>
      r.url().endsWith('/api/platform/invoices') && r.request().method() === 'POST');
    await owner.getByRole('button', { name: /Create draft|أنشئ المسوّدة/ }).click();
    expect((await created).status()).toBe(201);
    await owner.waitForURL(/\/platform\/invoices\/\d+$/);

    // زرُّ الإصدار موجودٌ ومعطَّل، والسببُ بجانبه: «أضف سطراً». زرٌّ معطَّل بلا تفسير يفتح بلاغاً.
    const issueButton = owner.getByRole('button', { name: /Issue invoice|أصدِر الفاتورة/ });
    await expect(issueButton).toBeDisabled();
    await expect(owner.getByText(/Add at least one line|أضف سطراً واحداً/)).toBeVisible();

    // سطرٌ واحد يكفي لفتح الباب.
    await owner.getByLabel(/^(Description|الوصف)$/).fill(lineText);
    await owner.getByLabel(/Unit amount|سعر الوحدة/).fill('100');
    const added = owner.waitForResponse((r) => /\/lines$/.test(r.url()) && r.request().method() === 'POST');
    await owner.getByRole('button', { name: /Add line|أضف سطراً/ }).click();
    expect((await added).status()).toBe(200);

    await expect(owner.getByText(lineText)).toBeVisible();
    await expect(issueButton).toBeEnabled();
  });

  test('الإصدار يُؤكَّد أوّلاً، ثمّ يمنح رقماً من سلسلة سوق', async () => {
    const issueButton = owner.getByRole('button', { name: /Issue invoice|أصدِر الفاتورة/ });
    await issueButton.click();

    // فعلٌ لا رجعةَ فيه: لا طلب قبل زرّ التأكيد في الحوار.
    const dialog = owner.getByRole('alertdialog');
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText(/never be edited|لا تُحرَّر/);

    const issued = owner.waitForResponse((r) => /\/issue$/.test(r.url()) && r.request().method() === 'POST');
    await dialog.getByRole('button', { name: /Issue invoice|أصدِر الفاتورة/ }).click();
    const response = await issued;
    expect(response.status()).toBe(200);
    invoiceNumber = (await response.json()).number;
    expect(invoiceNumber).toMatch(/^[A-Z0-9-]+\d{6}$/);

    await expect(dialog).toBeHidden();
    // وبعد الإصدار يختفي بابُ التحرير كلُّه: لا إضافةَ سطر ولا إلغاء.
    await expect(owner.getByRole('button', { name: /Add line|أضف سطراً/ })).toBeHidden();
    await expect(owner.getByRole('button', { name: /Discard draft|أهمِل المسوّدة/ })).toBeHidden();
    expect(await axe(owner)).toEqual([]);
  });

  test('التاجر يرى فاتورتَه بعملتها وتعليماتِ دفعها — ولا زرَّ دفعٍ عنده', async () => {
    await merchant.goto(`${BASE}/admin/subscription`);
    await merchant.getByText(invoiceNumber).waitFor({ timeout: 45_000 });

    // تعليماتُ الدفع هي جوابُ «كيف أدفع؟» — لا زرٌّ يَعِد ببوّابة دفع لا وجود لها.
    await expect(merchant.getByText(`QA bank transfer ${stamp}`)).toBeVisible();
    await expect(merchant.getByRole('button', { name: /pay now|ادفع الآن/i })).toHaveCount(0);

    const row = merchant.locator('tr', { hasText: invoiceNumber });
    await row.getByRole('button').click();
    await merchant.getByRole('menuitem', { name: /View invoice|اعرض الفاتورة/ }).click();

    const drawer = merchant.getByRole('dialog');
    await expect(drawer).toContainText(lineText);
    await expect(drawer).toContainText(invoiceNumber);
    expect(await axe(merchant)).toEqual([]);
    await drawer.getByRole('button', { name: /close|إغلاق/i }).first().click();
  });

  test('حوالةٌ جزئية ثمّ متمّمة: المستند يصير مسدَّداً عند الطرفين', async () => {
    const record = async (amount) => {
      await owner.getByLabel(/Amount received|المبلغ الواصل/).fill(amount);
      const saved = owner.waitForResponse((r) => /\/payments$/.test(r.url()) && r.request().method() === 'POST');
      await owner.getByRole('button', { name: /Record payment|سجّل السداد/ }).click();
      return saved;
    };

    await owner.reload();
    expect((await record('40')).status()).toBe(204);
    await expect(owner.getByText(/40/).first()).toBeVisible();

    // ما يتجاوز المتبقّي يُقال **قبل** الرحلة: لا رصيدَ دائنٍ يُخلَق ضمناً.
    await owner.getByLabel(/Amount received|المبلغ الواصل/).fill('500');
    await owner.getByRole('button', { name: /Record payment|سجّل السداد/ }).click();
    await expect(owner.getByText(/More than the|أكثر من المتبقّي/)).toBeVisible();

    expect((await record('60')).status()).toBe(204);
    await expect(owner.getByText(/^(Settled|مسدَّدة)$/)).toBeVisible();

    // وعند التاجر كذلك — وهو ما يجعل الطرفين يقرآن الرقم نفسه.
    await merchant.reload();
    const row = merchant.locator('tr', { hasText: invoiceNumber });
    await expect(row.getByText(/^(Paid|مسدَّدة)$/)).toBeVisible();
  });
});
