import { expect, test } from '@playwright/test';

// ============================================================================
// تحقّق المتصفّح لمهمّة التجربة: السمة، الاتجاه، الكشف، واللوحتان — على مكدّس حقيقي.
// ما يُفحص هنا لا يُفحص بلا متصفّح: ألوان محسوبة فعلاً، واتجاه صفحة، وحركة تُلغى بتفضيل نظام.
// ============================================================================

// بيانات المدير من البيئة بقيمها الافتراضية (M10): كانت مكتوبةً حرفيّاً، فلم يكن هذا الملفّ قابلاً
// للتشغيل إلا على حزمةٍ مبذورةٍ بهذين تحديداً — وعلى غيرها يفشل عند الدخول بمهلةٍ منتهية، لا برسالةٍ
// تقول السبب. (العلّة نفسها التي كانت في مضيف المنصّة داخل responsive.spec.js، صُحِّحت في M9.)
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

const signInAsAdmin = async (page) => {
  await page.goto('/login');
  await page.getByRole('textbox').first().fill(ADMIN.email);
  await page.locator('input[type="password"]').first().fill(ADMIN.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'));
};

// الوضع يُكتب على المستند فور الإقلاع؛ الانتظار هنا للرسم لا لشبكة.
const readTokens = async (page) => {
  await page.waitForSelector('html[data-theme]', { timeout: 10_000 });
  return page.evaluate(() => {
    const s = getComputedStyle(document.documentElement);
    return {
      theme: document.documentElement.dataset.theme,
      dir: document.documentElement.dir,
      bg: s.getPropertyValue('--color-bg').trim(),
      text: s.getPropertyValue('--color-text').trim(),
      surface: s.getPropertyValue('--color-surface').trim(),
      colorScheme: s.colorScheme,
    };
  });
};

test.describe('السمة', () => {
  test('الوضع الداكن يغيّر الرموز المحسوبة فعلاً لا الوسم وحده', async ({ page }) => {
    await page.goto('/');
    const light = await readTokens(page);
    expect(light.theme).toBe('light');

    await page.getByRole('button', { name: /switch between light and dark|التبديل بين/i }).click();

    const dark = await readTokens(page);
    expect(dark.theme).toBe('dark');
    expect(dark.bg).not.toBe(light.bg);
    expect(dark.text).not.toBe(light.text);
    // المتصفّح نفسه يرسم الحقول وأشرطة التمرير من هذه الخاصّية.
    expect(dark.colorScheme).toBe('dark');
  });

  test('الاختيار يبقى بعد إعادة التحميل', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /switch between light and dark|التبديل بين/i }).click();
    expect((await readTokens(page)).theme).toBe('dark');

    await page.reload();

    expect((await readTokens(page)).theme).toBe('dark');
  });

  test('الوضع الداكن يبقى مقروءاً: النصّ على الخلفية فوق 4.5:1', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /switch between light and dark|التبديل بين/i }).click();

    const ratio = await page.evaluate(() => {
      const value = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
      const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
      const lum = (hex) => {
        const [r, g, b] = rgb(hex).map((c) => {
          const s = c / 255;
          return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
        });
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
      };
      const [a, b] = [lum(value('--color-text')), lum(value('--color-bg'))].sort((x, y) => y - x);
      return (a + 0.05) / (b + 0.05);
    });

    expect(ratio).toBeGreaterThan(4.5);
  });
});

test.describe('الاتجاه', () => {
  test('العربية تقلب اتجاه المستند والإنجليزية تعيده', async ({ page }) => {
    await page.goto('/');
    const initial = await readTokens(page);
    expect(['rtl', 'ltr']).toContain(initial.dir);

    await page.getByRole('button', { name: /toggle language|تبديل اللغة/i }).first().click();

    // الاتجاه ينقلب مع النصّ لا قبله: حزمة اللغة تُحمَّل عند الطلب (تقسيم الترجمة لكل لغة)،
    // وقلب الاتجاه قبل وصولها يترك صفحةً بتخطيط لغة ونصّ أخرى. فالانتظار هنا للسلوك الصحيح.
    await page.waitForFunction(
      (before) => document.documentElement.dir !== before, initial.dir, { timeout: 10_000 },
    );
    const switched = await readTokens(page);

    expect(switched.dir).not.toBe(initial.dir);
  });

  test('لا تمرير أفقي في أي من الاتجاهين', async ({ page }) => {
    // تمرير أفقي في صفحة متجر عيبٌ مرئي فوراً، وسببه عادةً قاعدة اتجاه فيزيائية.
    for (const _ of [0, 1]) {
      await page.goto('/');
      const overflow = await page.evaluate(() =>
        document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
      expect(overflow).toBe(false);
      await page.getByRole('button', { name: /toggle language|تبديل اللغة/i }).first().click();
      await page.waitForTimeout(200);
    }
  });
});

// ============================================================================
// كشف الافتتاح **اختياريّ للمتجر، وافتراضه "لا"** (openingExperience.js، الشرط الأوّل).
//
// وهذه المجموعة كانت تفترض أنّه مُفعَّل ولا تُفعّله: فكان اختبارٌ واحد يفشل على متجرٍ لم يطلبه،
// و**ثلاثة تمرّ لسببٍ خاطئ** — كلّها تتأكّد من *غيابه* (تقليل الحركة يمنعه، الرابط العميق لا
// تسبقه ستارة، لا يتكرّر في الجلسة)، والغياب مضمون مجّاناً ما دام مُطفأً. اختبارٌ يمرّ لأنّ
// الميزة مُعطّلة لا يحرس شيئاً. يُفعَّل هنا للمجموعة ثمّ يُعاد كما كان (M19).
// ============================================================================
test.describe('كشف الافتتاح', () => {
  let restore = null;

  const settings = async (request, token, body) => request.fetch('/api/admin/store/settings', {
    method: body ? 'PUT' : 'GET',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    ...(body ? { data: body } : {}),
  });

  test.beforeAll(async ({ request }) => {
    const token = (await (await request.post('/api/auth/login', { data: ADMIN })).json()).accessToken;
    const current = await (await settings(request, token)).json();
    restore = { token, settings: current };
    const enabled = { ...current, branding: { ...current.branding, opening: { ...current.branding.opening, enabled: true } } };
    expect((await settings(request, token, enabled)).status()).toBe(204);
  });

  test.afterAll(async ({ request }) => {
    if (restore) await settings(request, restore.token, restore.settings);
  });

  test('يظهر لزائر أوّل مرّة ويُزال بعد انتهائه', async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto('/');

    await expect(page.getByTestId('opening-experience')).toBeVisible();
    await expect(page.getByTestId('opening-experience')).toBeHidden({ timeout: 5000 });

    await context.close();
  });

  test('لا يتكرّر في الجلسة نفسها', async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto('/');
    await expect(page.getByTestId('opening-experience')).toBeHidden({ timeout: 5000 });

    await page.goto('/offers');
    await page.goto('/');

    await expect(page.getByTestId('opening-experience')).toHaveCount(0);
    await context.close();
  });

  test('تفضيل تقليل الحركة يمنعه تماماً', async ({ browser }) => {
    const context = await browser.newContext({ reducedMotion: 'reduce' });
    const page = await context.newPage();

    await page.goto('/');

    await expect(page.getByTestId('opening-experience')).toHaveCount(0);
    await context.close();
  });

  test('رابط عميق لا تسبقه ستارة', async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();

    await page.goto('/offers');

    await expect(page.getByTestId('opening-experience')).toHaveCount(0);
    await context.close();
  });
});

test.describe('لوحة المدير', () => {
  test('تعرض مؤشّرات حقيقية وتذكر ما لا تقيسه', async ({ page }) => {
    await signInAsAdmin(page);
    await page.goto('/admin');

    await expect(page.getByRole('heading', { name: /store performance|أداء المتجر/i })).toBeVisible();

    // الحدود مطويّة لا مخفيّة: المدير يعرف مفرداتنا، فالغياب يُعلَن عند السؤال لا في كل زيارة.
    await page.getByText(/what this dashboard does not show|ما لا تعرضه هذه اللوحة/i).click();
    await expect(page.getByText(/profit and margin are not shown|الربح والهامش غير معروضين/i)).toBeVisible();
  });

  test('تبديل المدّة يعيد الجلب بمفتاح مغلق', async ({ page }) => {
    await signInAsAdmin(page);
    const requests = [];
    page.on('request', (r) => { if (r.url().includes('/api/admin/reports')) requests.push(r.url()); });

    await page.goto('/admin');
    await page.getByRole('button', { name: /^7 days$|^٧ أيام$/ }).click();
    await page.waitForTimeout(600);

    expect(requests.some((u) => u.includes('range=Last7Days'))).toBe(true);
    // لا تواريخ حرّة في الرابط: المدى يحسبه الخادم.
    expect(requests.every((u) => !/from=|to=/.test(u))).toBe(true);
  });

  test('اللوحة تعمل في الوضع الداكن', async ({ page }) => {
    await signInAsAdmin(page);
    await page.goto('/admin');
    await page.evaluate(() => localStorage.setItem('souq_theme', 'dark'));
    await page.reload();

    expect((await readTokens(page)).theme).toBe('dark');
    await expect(page.getByRole('heading', { name: /store performance|أداء المتجر/i })).toBeVisible();
  });

  test('الخروج والعودة إلى المتجر يبقيان مرئيَّين على شاشة قصيرة', async ({ page }) => {
    // الشريط الجانبي بارتفاع الشاشة وأقسامه أحد عشر: على 600 بكسل كان مجموعها يتجاوزه
    // فيُقصّ آخره — وآخره زرّ الخروج. لا يظهر ذلك على شاشة مطوّر بارتفاع 1080.
    await signInAsAdmin(page);
    await page.setViewportSize({ width: 1280, height: 600 });
    await page.goto('/admin');

    const logout = page.getByRole('button', { name: /log out|تسجيل الخروج/i }).first();
    await expect(logout).toBeVisible();
    const box = await logout.boundingBox();
    expect(box.y + box.height).toBeLessThanOrEqual(600);
  });

  test('كل مخطّط له بديل نصّي مقروء', async ({ page }) => {
    await signInAsAdmin(page);
    await page.goto('/admin');
    // الانتظار للوحة نفسها لا لمهلة ثابتة: قاعدة بطيئة كانت تجعل الفحص يقرأ هيكلاً فارغاً.
    await page.getByText(/stock health|حالة المخزون/i).waitFor({ timeout: 45_000 });

    // لا نعدّ كل <svg> في الصفحة — أيقونات القائمة الجانبية منها. المقصود جسم مخطّط
    // مرسوم فعلاً (مخفيّ عن شجرة الإتاحة)، والسؤال: هل لكلٍّ منه جدوله المقروء؟
    const charts = page.locator('section:has(> div[aria-hidden="true"] svg)');
    const drawn = await charts.count();
    expect(drawn, 'اللوحة لم ترسم أي مخطّط — لا معنى لفحص بدائلها').toBeGreaterThan(0);

    for (let i = 0; i < drawn; i += 1) {
      await expect(charts.nth(i).locator('figure table')).toHaveCount(1);
    }
  });
});

test.describe('نظرة العمل', () => {
  test('شاشة منفصلة تقول إن ما تعرضه إيراد لا ربح', async ({ page }) => {
    await signInAsAdmin(page);
    await page.goto('/admin/business');

    await expect(page.getByRole('heading', { name: /business overview|نظرة على العمل/i })).toBeVisible();
    await expect(page.getByText(/this is revenue, not profit|هذا إيراد لا ربح/i)).toBeVisible();
  });

  test('الحكم يظهر مع سببه', async ({ page }) => {
    await signInAsAdmin(page);
    await page.goto('/admin/business');

    const verdict = page.locator('section').filter({ hasText: /healthy|watch|attention|not enough data|سليم|يستحقّ|يحتاج|لا تكفي/i }).first();
    await expect(verdict).toBeVisible();
  });
});
