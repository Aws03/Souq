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
const ADMIN = { email: 'admin@souq.com', password: 'Admin@123' };

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
    for (const path of ['/admin', '/admin/business', '/admin/products', '/admin/orders']) {
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

  test('هدف اللمس في الشريط السفلي لا يقلّ عن 40 بكسل', async ({ page }) => {
    await page.goto('/admin');
    const tab = page.getByRole('link', { name: /Business/i }).first();
    const box = await tab.boundingBox();
    expect(box.height).toBeGreaterThanOrEqual(40);
    expect(box.width).toBeGreaterThanOrEqual(40);
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
