import { expect, test } from '@playwright/test';

// ============================================================================
// واجهة الإدارة على هاتف (مشروع `phone`، Pixel 7 ≈ 393px).
//
// لماذا فحص منفصل؟ لأن لوحة الإدارة بُنيت لسطح المكتب: شريط جانبي، وجداول عريضة، وشبكات
// من أربعة أعمدة. وما يكسرها على هاتف لا يُرى في اختبار مكوّن ولا في لقطة سطح مكتب —
// يُرى في تمرير أفقي، أو تسمية مقصوصة إلى ثلاثة أحرف، أو هدف لمس أصغر من الإصبع.
//
// والتاجر الذي يدير متجره من جيبه هو الحالة العادية لا الاستثناء.
// ============================================================================
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

const signIn = async (page) => {
  await page.goto('/login');
  await page.locator('input[type="email"]').first().fill(ADMIN.email);
  await page.locator('input[type="password"]').first().fill(ADMIN.password);
  await page.getByRole('button', { name: /sign in|دخول/i }).click();
  await page.waitForURL((url) => !url.pathname.includes('/login'));
};

// التمرير الأفقي للصفحة كلّها: العيب. أمّا حاوية تمرّر محتواها وحدها (جدول، مخطّط) فهي الحلّ.
const pageOverflows = (page) => page.evaluate(() =>
  document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);

test.describe('المتجر على هاتف', () => {
  test('لا تمرير أفقي في الاتجاهين', async ({ page }) => {
    for (const _ of [0, 1]) {
      await page.goto('/');
      await page.waitForSelector('article a[href^="/products/"]', { timeout: 45_000 });
      expect(await pageOverflows(page)).toBe(false);

      // على الهاتف تبديل اللغة داخل القائمة المنسدلة لا في الشريط العلوي.
      await page.getByRole('button', { name: /open menu|فتح القائمة/i }).click();
      await page.getByRole('button', { name: /toggle language|تبديل اللغة/i }).click();
      await page.waitForTimeout(600);
    }
  });
});

test.describe('لوحة الإدارة على هاتف', () => {
  test.beforeEach(async ({ page }) => { await signIn(page); });

  test('لا تمرير أفقي في أي من شاشات الإدارة', async ({ page }) => {
    for (const path of ['/admin', '/admin/business', '/admin/products', '/admin/orders', '/admin/inventory', '/admin/search-synonyms', '/admin/settings', '/admin/staff']) {
      await page.goto(path);
      await page.waitForTimeout(2500);
      expect(await pageOverflows(page), `تمرير أفقي في ${path}`).toBe(false);
    }
  });

  test('تسميات شريط التبويب كاملة لا مقصوصة إلى ثلاثة أحرف', async ({ page }) => {
    await page.goto('/admin');
    const products = page.getByRole('link', { name: /^Products$|^المنتجات$/ });
    await products.waitFor({ timeout: 45_000 });

    // القصّ بنقاط كان يحوّل "Products" إلى "Pro…" حين ضاق الشريط بأحد عشر قسماً.
    const label = (await products.textContent()).trim();
    expect(label).toMatch(/^(Products|المنتجات)$/);
    expect(label).not.toContain('\u2026');
  });

  test('كل أقسام الإدارة موجودة في الشريط ولو احتاجت تمريراً', async ({ page }) => {
    await page.goto('/admin');
    for (const section of [/Dashboard|Home/i, /Business/i, /Products/i, /Orders/i, /Payments/i]) {
      await expect(page.getByRole('link', { name: section }).first()).toBeAttached();
    }
  });

  // ============================================================================
  // ألسنة شاشة البحث على هاتف (M13): لسانان قصيران، لكن المكوّن عامّ وسيحمل ألسنةً أكثر غداً — فالقاعدة
  // تُثبَّت الآن: الألسنة **تُزحلق ولا تُلفّ** (سطران يكسران الخطّ السفلي)، والصفحة نفسها لا تمرّر أفقياً،
  // وهدف اللمس لا يقلّ عن 44 بكسلاً (WCAG 2.5.8) — وهو ما لا يسري إلا في محاكاة جهازٍ حقيقيّ، أي هنا.
  // ============================================================================
  test('ألسنة شاشة البحث: هدف لمسٍ كافٍ، وزحلقةٌ لا لفّ، وبلا تمرير أفقي للصفحة', async ({ page }) => {
    await page.goto('/admin/search-synonyms');
    const tabs = page.getByRole('tab');
    await tabs.first().waitFor({ timeout: 45_000 });

    const boxes = await tabs.evaluateAll((nodes) => nodes.map((n) => n.getBoundingClientRect().toJSON()));
    expect(boxes.length).toBeGreaterThan(1);
    for (const box of boxes) expect(box.height, 'هدف لمس اللسان').toBeGreaterThanOrEqual(44);

    // كلّها على سطرٍ واحد: تساوي أعلى الصناديق يعني أنّها لم تُلفّ.
    const tops = new Set(boxes.map((b) => Math.round(b.top)));
    expect(tops.size, 'الألسنة لُفّت إلى أكثر من سطر بدل أن تُزحلق').toBe(1);

    expect(await pageOverflows(page), 'شاشة البحث تمرّر أفقياً').toBe(false);

    // والجدول يمرّر داخل حاويته لا بالصفحة — نفس قاعدة بقيّة جداول الإدارة.
    await page.getByRole('tab').last().click();
    await page.waitForTimeout(1500);
    expect(await pageOverflows(page), 'لسان المفردات يمرّر أفقياً').toBe(false);
  });

  test('هدف اللمس في الشريط السفلي لا يقلّ عن 40 بكسل', async ({ page }) => {
    await page.goto('/admin');
    const tab = page.getByRole('link', { name: /Business/i }).first();
    const box = await tab.boundingBox();
    expect(box.height).toBeGreaterThanOrEqual(40);
    expect(box.width).toBeGreaterThanOrEqual(40);
  });

  test('زرّ إجراءات الصفّ إصبعٌ يلمسه لا فأرةٌ تدقّ عليه', async ({ page }) => {
    // هذا الزرّ هو المدخل الوحيد لكل إجراء على صفّ: بلا لمسه لا يُفتح طلب ولا يُصحَّح مخزون. وُجد 32px
    // في M7 على شاشة الجرد، وهو مشترك في كل جداول اللوحة — فالقياس هنا يحرسها كلّها لا واحدة.
    // المشروع `phone` يحاكي جهازاً حقيقياً، وهو ما يجعل `pointer: coarse` يسري فعلاً.
    let measured = 0;
    for (const path of ['/admin/inventory', '/admin/products', '/admin/orders']) {
      await page.goto(path);
      const triggers = page.locator('table tbody tr button');
      // انتظار محدود ثم تجاوز: متجرٌ تجريبي قد لا يحمل طلبات بعد، وجدولٌ فارغ ليس عيباً يُقاس.
      await triggers.first().waitFor({ timeout: 20_000 }).catch(() => {});
      if (await triggers.count() === 0) continue;

      const box = await triggers.first().boundingBox();
      expect(Math.min(box.width, box.height), `هدف اللمس في ${path}: ${box.width}×${box.height}`)
        .toBeGreaterThanOrEqual(40);
      measured += 1;
    }
    // وإلّا مرّ الاختبار فراغاً: "لا جدول فيه صفوف" ليس "كل الأهداف سليمة".
    expect(measured, 'لم يُقَس أي جدول — لا يجوز أن يمرّ هذا الاختبار بلا قياس').toBeGreaterThan(0);
  });

  test('جدول عريض يمرّر داخل حاويته لا داخل الصفحة', async ({ page }) => {
    await page.goto('/admin/orders');
    await page.waitForTimeout(3000);
    expect(await pageOverflows(page)).toBe(false);

    const table = page.locator('table').first();
    if (await table.count() > 0) {
      // الجدول نفسه قد يكون أعرض من الشاشة — والحاوية هي من تمرّره.
      const scrolls = await page.evaluate(() => {
        const found = document.querySelector('table');
        if (!found) return true;
        for (let node = found.parentElement; node; node = node.parentElement) {
          const overflow = getComputedStyle(node).overflowX;
          if (overflow === 'auto' || overflow === 'scroll') return true;
        }
        return found.scrollWidth <= found.clientWidth + 1;
      });
      expect(scrolls, 'جدول أعرض من الشاشة بلا حاوية تمرّره').toBe(true);
    }
  });

  test('إعدادات المتجر على هاتف: شريط الحفظ فوق شريط التبويب، والمعاينة داخل الشاشة، في الاتجاهين', async ({ page }) => {
    for (const language of ['en', 'ar']) {
      await page.evaluate((lang) => localStorage.setItem('souq_lang', lang), language);
      await page.goto('/admin/settings');
      await page.locator('#settings-colors-primary').waitFor({ timeout: 45_000 });
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(language === 'ar' ? 'rtl' : 'ltr');
      expect(await pageOverflows(page), `تمرير أفقي بالاتجاه ${language}`).toBe(false);

      // شريط الحفظ يلتصق بأسفل الشاشة؛ إن غطّاه شريط التبويب الثابت صار "حفظ" زرّاً لا يُلمس.
      const save = await page.getByRole('region', { name: /Save changes|حفظ التعديلات/ }).boundingBox();
      const tabs = await page.locator('nav').filter({ has: page.getByRole('link', { name: /^(Home|الرئيسية)$/ }) }).last().boundingBox();
      expect(save.y + save.height, 'شريط الحفظ تحت شريط التبويب').toBeLessThanOrEqual(tabs.y + 1);

      const width = page.viewportSize().width;
      const preview = await page.locator('figure').boundingBox();
      expect(preview.x).toBeGreaterThanOrEqual(-1);
      expect(preview.x + preview.width).toBeLessThanOrEqual(width + 1);
    }
    await page.evaluate(() => localStorage.setItem('souq_lang', 'en'));
  });

  test('اللوحة تُقرأ على هاتف: المؤشّرات والمخطّطات تتراصّ ولا تُقصّ', async ({ page }) => {
    await page.goto('/admin');
    await page.getByText(/stock health|حالة المخزون/i).waitFor({ timeout: 45_000 });
    expect(await pageOverflows(page)).toBe(false);

    // كل مخطّط داخل عرض الشاشة — لا نصف حلقة خارج الحافّة.
    const width = page.viewportSize().width;
    for (const section of await page.locator('section:has(> div[aria-hidden="true"] svg)').all()) {
      const box = await section.boundingBox();
      expect(box.x).toBeGreaterThanOrEqual(-1);
      expect(box.x + box.width).toBeLessThanOrEqual(width + 1);
    }
  });
});

// ============================================================================
// منطقة المنصّة على هاتف: قائمة المتاجر وصفحة متجر وخطوة إنشاء وحسابات المنصّة وسجلّ النشاط — بلا تمرير أفقي في الاتجاهين، والجدول العريض
// يمرّر داخل حاويته. مالك منصّة يتابع تجهيز متجر عميل من هاتفه حالة عادية كالتاجر.
// ============================================================================
test.describe('منطقة المنصّة على هاتف', () => {
  // ============================================================================
  // مضيف المنصّة يُشتقّ من baseURL ولا يُكتب حرفياً. كان مكتوباً `http://admin.localhost:5173`، أي
  // منفذ خادم التطوير وحده — فهذا الاختبار **لم يكن قابلاً للتشغيل على حزمة الحاويات إطلاقاً**، وهو
  // ما اكتشفه تشغيله في M7 (فشل على الاتصال لا على قياس). المنصّة تُخدَم على `admin.<المضيف>` بمنفذ
  // الحزمة نفسه (PLATFORM_HOST في docker-compose.yml)، وهذا ما يبنيه السطر التالي.
  // ============================================================================
  const OWNER = {
    email: process.env.SOUQ_E2E_OWNER_EMAIL || 'owner@souq.com',
    password: process.env.SOUQ_E2E_OWNER_PASSWORD || 'Owner@12345',
  };

  const platformOrigin = (base) => {
    const url = new URL(base);
    if (!url.hostname.startsWith('admin.')) url.hostname = `admin.${url.hostname}`;
    return url.origin;
  };

  test('المتاجر، صفحة متجر، إنشاء متجر، الحسابات والسجلّ — بلا تمرير أفقي في الاتجاهين', async ({ page }, testInfo) => {
    const PLATFORM = platformOrigin(testInfo.project.use.baseURL);
    await page.goto(`${PLATFORM}/login`);
    await page.locator('input[type="email"]').fill(OWNER.email);
    await page.locator('input[type="password"]').fill(OWNER.password);
    await page.getByRole('button', { name: /sign in|دخول/i }).click();
    await page.waitForURL((url) => !url.pathname.includes('/login'));

    for (const language of ['en', 'ar']) {
      await page.evaluate((lang) => localStorage.setItem('souq_lang', lang), language);
      await page.goto(`${PLATFORM}/platform/stores`);
      const firstStore = page.locator('tbody a[href^="/platform/stores/"]').first();
      await firstStore.waitFor({ timeout: 45_000 });
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(language === 'ar' ? 'rtl' : 'ltr');
      expect(await pageOverflows(page), `قائمة المتاجر ${language}`).toBe(false);

      await firstStore.click();
      await page.getByRole('heading', { level: 2 }).first().waitFor({ timeout: 30_000 });
      expect(await pageOverflows(page), `صفحة متجر ${language}`).toBe(false);

      await page.goto(`${PLATFORM}/platform/stores/new`);
      await page.locator('input').first().waitFor({ timeout: 30_000 });
      expect(await pageOverflows(page), `متجر جديد ${language}`).toBe(false);

      await page.goto(`${PLATFORM}/platform/accounts`);
      await page.locator('tbody tr').first().waitFor({ timeout: 30_000 });
      expect(await pageOverflows(page), `حسابات المنصّة ${language}`).toBe(false);

      await page.goto(`${PLATFORM}/platform/audit`);
      await page.getByRole('status').waitFor({ timeout: 30_000 });
      expect(await pageOverflows(page), `سجلّ النشاط ${language}`).toBe(false);
    }
    await page.evaluate(() => localStorage.setItem('souq_lang', 'en'));
  });
});
