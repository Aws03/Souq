import { expect, test } from '@playwright/test';

// ============================================================================
// تغيير كلمة المرور من حساب العميل، في متصفّحين حقيقيين (M9، TD-29).
//
// الشاشة نفسها لم تكن موجودة قبل هذه المرحلة: النقطة والمدقّق ودالّة العميل كلّها كانت جاهزة، ولا
// شاشة تستدعيها — فعميلٌ مسجَّل لم يكن يستطيع تغيير كلمة مروره إلا بالخروج وطلب رابط على بريده.
//
// **ولماذا سياقان لا واحد؟** لأن الضمانة التي تُعلنها الشاشة ("يُخرجك من كل الأجهزة الأخرى") لا
// تُقاس في سياق واحد إطلاقاً. جهاز ثانٍ مسجَّل الدخول هو الطريقة الوحيدة لإثبات أنّ جلسته انتهت
// فعلاً — والاختبار يفحص **كلا** الطريقين اللذين تنتهي بهما: توكن الوصول (طلبٌ محميّ يُرفض) ورمز
// التجديد (الصفحة لا تستعيد الجلسة بعد إعادة تحميل).
//
// الثاني هو ما وجد ثقباً حقيقياً في M9: رمز تجديد استُهلك قبل ثوانٍ كان ينجو من تغيير كلمة المرور،
// فيصنع جلسة جديدة صالحة. أُغلق في RefreshSession، ومُثبَّت في AuthSessionTests.
//
// يُشغَّل على حزمة الحاويات:
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/account-password.spec.js --project=desktop
// ============================================================================
const stamp = Date.now().toString(36);
const FIRST = 'Account-Pass-1';
const SECOND = 'Account-Pass-2';

test.describe.configure({ mode: 'serial' });

const account = { email: `m9pw${stamp}@souq.test`, name: `M9 Password ${stamp}` };

async function signUp(page) {
  await page.goto('/register', { waitUntil: 'networkidle' });
  await page.getByRole('textbox').nth(0).fill(account.name);
  await page.getByRole('textbox').nth(1).fill(account.email);
  const passwords = page.locator('input[type="password"]');
  await passwords.nth(0).fill(FIRST);
  await passwords.nth(1).fill(FIRST);
  await page.getByRole('button', { name: /create account|إنشاء الحساب/i }).click();
  await page.waitForURL(/\/$|\/account/, { timeout: 30_000 });
}

async function signIn(page, password) {
  await page.goto('/login', { waitUntil: 'networkidle' });
  await page.locator('input[type="email"]').first().fill(account.email);
  await page.locator('input[type="password"]').first().fill(password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 30_000 });
}

const fillForm = async (page, { current, next, confirm }) => {
  await page.getByLabel(/^Current password$/i).fill(current);
  await page.getByLabel(/^New password$/i).fill(next);
  await page.getByLabel(/^Confirm password$/i).fill(confirm ?? next);
};

test('the password screen is reachable from the account navigation and refuses a wrong current password', async ({ browser }, testInfo) => {
  testInfo.setTimeout(180_000);
  const context = await browser.newContext();
  const page = await context.newPage();
  await signUp(page);

  // يُوصَل إليها بالتنقّل لا بعنوان مكتوب: قسمٌ لا يُرى في القشرة قسمٌ غير موجود عملياً.
  await page.goto('/account', { waitUntil: 'networkidle' });
  await page.getByRole('link', { name: /^Password$/i }).click();
  await expect(page).toHaveURL(/\/account\/password$/);
  await expect(page.getByRole('heading', { name: /^Password$/i })).toBeVisible();

  // الضمانة مكتوبة قبل الزرّ: أثرٌ على أجهزة أخرى يُقرأ قبل الالتزام به.
  await expect(page.getByText(/signs you out everywhere else/i)).toBeVisible();

  // كلمة حالية خاطئة: رسالة على الحقل، والمستخدم **يبقى داخلاً** — الخادم يعيدها 400 لا 401 عمداً.
  await fillForm(page, { current: 'Wrong-Pass-9', next: SECOND });
  await page.getByRole('button', { name: /^Change password$/i }).click();
  await expect(page.getByText(/current password is incorrect/i)).toBeVisible({ timeout: 20_000 });
  await expect(page).toHaveURL(/\/account\/password$/);
  await page.reload({ waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: /^Password$/i })).toBeVisible();

  await context.close();
});

test('a weak new password is refused in the browser before any request is sent', async ({ browser }, testInfo) => {
  testInfo.setTimeout(180_000);
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, FIRST);
  await page.goto('/account/password', { waitUntil: 'networkidle' });

  const attempts = [];
  page.on('request', (r) => {
    if (r.method() === 'POST' && new URL(r.url()).pathname === '/api/auth/change-password') attempts.push(r.url());
  });

  // ثمانية أحرف بلا رقم: يرفضها الخادم (PasswordRules)، والواجهة تقول ذلك **بلا رحلة**.
  await fillForm(page, { current: FIRST, next: 'aaaaaaaa' });
  await page.getByRole('button', { name: /^Change password$/i }).click();
  await expect(page.getByText(/both letters and numbers/i)).toBeVisible();
  expect(attempts.length, 'لا طلب يُرسل لنموذج ترفضه القواعد').toBe(0);

  await context.close();
});

test('changing the password signs out another device, on both the access token and the refresh cookie', async ({ browser }, testInfo) => {
  testInfo.setTimeout(180_000);

  // جهازان، سياقان مستقلّان تماماً (ملفات تعريف ارتباط منفصلة) — وهذا هو جوهر الاختبار.
  const deviceA = await browser.newContext();
  const deviceB = await browser.newContext();
  const a = await deviceA.newPage();
  const b = await deviceB.newPage();

  await signIn(a, FIRST);
  await signIn(b, FIRST);
  // الجهاز الثاني داخلٌ فعلاً: صفحة محميّة تُعرض له.
  await b.goto('/account', { waitUntil: 'networkidle' });
  await expect(b.getByRole('heading', { name: /^My account$/i })).toBeVisible({ timeout: 30_000 });

  // الجهاز الأول يغيّر كلمة المرور.
  await a.goto('/account/password', { waitUntil: 'networkidle' });
  await fillForm(a, { current: FIRST, next: SECOND });
  await a.getByRole('button', { name: /^Change password$/i }).click();
  await expect(a.getByText(/password was changed/i)).toBeVisible({ timeout: 20_000 });
  // والحقول فُرِّغت: لا كلمة مرور باقية في الشاشة بعد إتمام العملية.
  await expect(a.getByLabel(/^Current password$/i)).toHaveValue('');

  // الجهاز الأول يبقى داخلاً — من غيّر كلمة مروره لا يُطرد بها.
  await a.goto('/account', { waitUntil: 'networkidle' });
  await expect(a.getByRole('heading', { name: /^My account$/i })).toBeVisible();

  // والثاني خرج: إعادة تحميل صفحة محميّة تنتهي عند تسجيل الدخول، لا عند الحساب. إعادة التحميل تجرّب
  // **رمز التجديد** أيضاً (التجديد الصامت عند الإقلاع)، فهذا يغطّي الطريقين معاً.
  await b.goto('/account', { waitUntil: 'networkidle' });
  await b.waitForURL(/\/login/, { timeout: 30_000 });

  // وكلمة المرور الجديدة هي التي تعمل الآن، والقديمة لا.
  await b.goto('/login', { waitUntil: 'networkidle' });
  await b.locator('input[type="email"]').first().fill(account.email);
  await b.locator('input[type="password"]').first().fill(FIRST);
  await b.getByRole('button', { name: /sign in|دخول/i }).click();
  await expect(b.getByText(/email or password is incorrect/i)).toBeVisible({ timeout: 20_000 });

  await signIn(b, SECOND);
  await b.goto('/account', { waitUntil: 'networkidle' });
  await expect(b.getByRole('heading', { name: /^My account$/i })).toBeVisible();

  await deviceA.close();
  await deviceB.close();
});
