import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// ============================================================================
// المسح المنهجي لواجهة المتجر (M4) — كل مسار × كل عرض × كل لغة × كل سمة.
//
// `responsive.spec.js` يفحص رحلة هاتف واحدة بعمق؛ هذا الملف يفحص **الاتّساع**: أنّ أي صفحة، بأي لغة، على أي
// عرض، لا تفيض أفقياً، ولا تُخرج زرّاً خارج النافذة، ولا تترك هدف لمس أصغر من الحدّ، ولا تُخالف axe.
//
// لماذا هذا الشكل بدل اختبار لكل تركيبة: العيوب التي وجدها هذا المسح (شريط تنقّل يقصّ زرّ السلة بين 768px
// و940px، شبكة دفع لا تنكمش تحت محتواها فيخرج زرّ المتابعة، صفّ سلّة بخمسة أعمدة لا تتّسع في 320px، زرّ حذف
// 16×19px) لم يظهر أيّ منها على عرض واحد أو لغة واحدة. المصفوفة **هي** الاختبار.
//
// يُشغَّل على حزمة الحاويات أو على خادم التطوير:
//   SOUQ_E2E_BASE_URL=http://localhost:8091 npx playwright test e2e/responsive-storefront.spec.js --project=desktop
// ============================================================================
const require = createRequire(import.meta.url);
const AXE = require.resolve('axe-core/axe.min.js');

// 320: أضيق هاتف عملي. 768: بداية اللوح — وهي نقطة العطل التي وجدها المسح. 1280: سطح مكتب. 2560: شاشة عريضة.
const VIEWPORTS = [
  { name: '320', width: 320, height: 720 },
  { name: '768', width: 768, height: 1024 },
  { name: '1280', width: 1280, height: 800 },
  { name: '2560', width: 2560, height: 1400 },
];

const PUBLIC_ROUTES = [
  ['home', '/'],
  ['offers', '/offers'],
  ['catalog filtered', '/?cats=1'],
  ['search results', '/?q=%D9%85%D9%83%D9%86%D8%B3%D8%A9'],
  ['search no results', '/?q=%D8%B2%D9%82%D9%81%D9%88%D9%86%D9%8A%D8%A7%D8%AA'],
  ['product detail', '/products/1'],
  ['login', '/login'],
  ['register', '/register'],
  ['not found', '/definitely-not-a-route'],
];

// تُزار بحساب زبون وسلّة فيها عدّة أسطر: سلّة بسطر واحد لا تكشف عيوب تكشفها سلّة حقيقية.
const PRIVATE_ROUTES = [
  ['cart with lines', '/cart'],
  ['checkout', '/checkout'],
  ['account profile', '/account'],
  ['account addresses', '/account/addresses'],
  ['my orders', '/orders'],
  ['wishlist', '/wishlist'],
];

// هدف اللمس الأدنى في WCAG 2.5.8. DesignSystem.md §11 يطلب 40px للهاتف؛ هذا الحدّ الأدنى المُلزِم.
const MIN_TARGET = 24;

// مدير المتجر — القيم الافتراضية هي بذرة التطوير (كما في back-office.spec.js). حزمة حاويات بوضع Production
// تفرض كلمة مرور ≥ 12 محرفاً، فتُمرَّر عندها بمتغيّرات بيئة بدل تعديل الملف.
const ADMIN = {
  email: process.env.SOUQ_E2E_ADMIN_EMAIL || 'admin@souq.com',
  password: process.env.SOUQ_E2E_ADMIN_PASSWORD || 'Admin@123',
};

// ============================================================================
// نصّ طويل جدّاً بلغتين. اسم قصير مبذور لا يكشف شيئاً: الفيض الأفقي يأتي من المحتوى الحقيقي — اسم منتج
// طويل في بطاقة، أو عنوان لا ينكسر في ترويسة. يُنشأ منتج واحد بهذا الاسم ويُزار كأي مسار آخر.
// ============================================================================
const LONG_AR = 'مكنسة كهربائية لاسلكية عمودية متعددة الاستخدامات بشفط إعصاري قوي وبطارية ليثيوم طويلة العمر وفلتر هيبا';
const LONG_EN = 'Cordless Upright Multi Surface Vacuum Cleaner With Cyclonic Suction Long Life Lithium Battery And HEPA Filter';

async function createLongNamedProduct(request, base) {
  const login = await request.post(`${base}/api/auth/login`, { data: ADMIN });
  if (!login.ok()) return null;
  const token = (await login.json()).accessToken ?? (await login.json()).token;
  if (!token) return null;
  const headers = { Authorization: `Bearer ${token}` };

  const categories = await request.get(`${base}/api/admin/categories`, { headers });
  if (!categories.ok()) return null;
  const categoryId = (await categories.json())[0]?.id;
  if (!categoryId) return null;

  const slug = `m4-long-name-${Date.now()}`;
  const created = await request.post(`${base}/api/products`, {
    headers,
    data: {
      categoryId, slug, price: 129.9, stockQuantity: 5,
      translations: { ar: { name: LONG_AR, description: LONG_AR }, en: { name: LONG_EN, description: LONG_EN } },
    },
  });
  return created.ok() ? slug : null;
}

// ============================================================================
// يقيس صفحةً واحدة. العناصر داخل حاويات تمرير أفقي مقصودة (صفوف الاكتشاف، شريط الإعلان، شريط الفئات)
// تُستثنى: خروجها عن النافذة سلوكها الصحيح لا عطل. والروابط داخل نصّ مستثناة من حدّ هدف اللمس، كما تستثنيها
// WCAG صراحةً (الهدف "داخل سطر" لا يُقاس بهذا المعيار).
// ============================================================================
async function measure(page) {
  return page.evaluate((minTarget) => {
    const vw = window.innerWidth;

    const inScroller = (el) => {
      for (let node = el.parentElement; node; node = node.parentElement) {
        const style = getComputedStyle(node);
        if (/(auto|scroll)/.test(style.overflowX)) return true;
        if (style.overflow === 'hidden' && node.scrollWidth > node.clientWidth + 1) return true;
      }
      return false;
    };

    const name = (el) => {
      const cls = typeof el.className === 'string' ? el.className.split(' ')[0] : '';
      const label = (el.getAttribute('aria-label') || el.textContent || '').trim().slice(0, 30);
      return `${el.tagName.toLowerCase()}${cls ? '.' + cls : ''}${label ? ` "${label}"` : ''}`;
    };

    const offscreen = [];
    const undersized = [];

    // الأزرار والحقول أهداف قائمة بذاتها؛ <a> قد يكون رابطاً داخل جملة، فيُقاس الخروج عن النافذة وحده.
    document.querySelectorAll('a, button, input, select, textarea, [role="button"]').forEach((el) => {
      const rect = el.getBoundingClientRect();
      if (rect.width === 0 || rect.height === 0) return;
      if (getComputedStyle(el).visibility === 'hidden') return;

      if (!inScroller(el) && (rect.right > vw + 1 || rect.left < -1)) offscreen.push(name(el));

      if (el.tagName === 'A') return;
      // عنصر معطَّل ليس هدفاً: لا يُضغط أصلاً، وWCAG 2.5.8 تستثنيه. (نجوم العرض-فقط مثلاً.)
      if (el.disabled) return;
      // الحقل النصّي: مساحة الضغط هي غلافه المُبطَّن لا صندوق محتواه، فيُقاس الغلاف.
      const box = el.tagName === 'INPUT' && el.parentElement
        ? el.parentElement.getBoundingClientRect() : rect;
      if (box.height < minTarget || box.width < minTarget) {
        undersized.push(`${name(el)} ${Math.round(box.width)}x${Math.round(box.height)}`);
      }
    });

    return {
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      widest: (() => {
        let worst = null;
        document.querySelectorAll('*').forEach((el) => {
          if (inScroller(el)) return;
          const rect = el.getBoundingClientRect();
          if (rect.width === 0) return;
          const past = Math.max(rect.right - vw, -rect.left);
          if (past > 1 && (!worst || past > worst.past)) worst = `${name(el)} (+${Math.round(past)}px)`;
        });
        return worst;
      })(),
      offscreen: [...new Set(offscreen)].slice(0, 6),
      undersized: [...new Set(undersized)].slice(0, 6),
    };
  }, MIN_TARGET);
}

async function axeViolations(page) {
  await page.addScriptTag({ path: AXE });
  return page.evaluate(async () => {
    const result = await window.axe.run(document, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
      rules: { 'color-contrast': { enabled: false } },
    });
    return result.violations.map((v) => `${v.id} (${v.nodes.length} node(s))`);
  });
}

async function contextFor(browser, lang, theme) {
  const context = await browser.newContext();
  await context.addInitScript(([l, t]) => {
    try { localStorage.setItem('souq_lang', l); localStorage.setItem('souq_theme', t); } catch { /* private */ }
  }, [lang, theme]);
  return context;
}

// زبون جديد وسلّة فيها أسطر — الحالة التي تكشف عيوب الجداول والشبكات.
async function signUpAndFillCart(page, base) {
  const stamp = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  await page.goto(`${base}/register`, { waitUntil: 'networkidle' });
  await page.getByRole('textbox').nth(0).fill('M4 Matrix');
  await page.getByRole('textbox').nth(1).fill(`m4matrix+${stamp}@souq.test`);
  const passwords = page.locator('input[type="password"]');
  await passwords.nth(0).fill('Matrix@12345');
  await passwords.nth(1).fill('Matrix@12345');
  await page.getByRole('button', { name: /create account|إنشاء الحساب/i }).click();
  await page.waitForURL(/\/$|\/account/, { timeout: 20_000 });

  for (const id of [1, 2, 3]) {
    await page.goto(`${base}/products/${id}`, { waitUntil: 'networkidle' });
    const add = page.getByRole('button', { name: /add to cart|أضف للسلة/i }).first();
    if (await add.isEnabled().catch(() => false)) await add.click();
    await page.waitForTimeout(300);
  }
}

test.describe.configure({ mode: 'serial' });

for (const lang of ['ar', 'en']) {
  test(`every storefront route holds its layout at every width — ${lang}`, async ({ browser, request }, testInfo) => {
    testInfo.setTimeout(240_000);
    const base = testInfo.project.use.baseURL;

    // منتج باسم طويل جدّاً بلغتين — المادة التي يظهر عليها الفيض، لا الأسماء القصيرة المبذورة.
    const longSlug = await createLongNamedProduct(request, base);
    expect(longSlug, 'تعذّر إنشاء منتج الاسم الطويل: تحقّق من SOUQ_E2E_ADMIN_EMAIL/PASSWORD').toBeTruthy();

    const context = await contextFor(browser, lang, 'light');
    const page = await context.newPage();
    await signUpAndFillCart(page, base);

    const routes = [...PUBLIC_ROUTES, ...PRIVATE_ROUTES, ['long product name', `/products/${longSlug}`]];
    const failures = [];
    for (const viewport of VIEWPORTS) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      for (const [name, path] of routes) {
        await page.goto(base + path, { waitUntil: 'networkidle' });
        const r = await measure(page);
        const at = `${lang} @${viewport.name} ${name}`;
        if (r.overflow > 0) failures.push(`${at}: page scrolls horizontally by ${r.overflow}px — widest: ${r.widest}`);
        if (r.offscreen.length) failures.push(`${at}: control outside the viewport — ${r.offscreen.join(' | ')}`);
        if (r.undersized.length) failures.push(`${at}: target smaller than ${MIN_TARGET}px — ${r.undersized.join(' | ')}`);
      }
    }
    await context.close();

    expect(failures, `\n${failures.join('\n')}\n`).toEqual([]);
  });
}

for (const lang of ['ar', 'en']) {
  for (const theme of ['light', 'dark']) {
    test(`every storefront route is axe-clean — ${lang}/${theme}`, async ({ browser }, testInfo) => {
      testInfo.setTimeout(240_000);
      const base = testInfo.project.use.baseURL;
      const context = await contextFor(browser, lang, theme);
      const page = await context.newPage();
      await signUpAndFillCart(page, base);

      const failures = [];
      // العرضان الطرفان: عيوب الوصول التي تعتمد على التخطيط تظهر على الضيّق، وبقيّتها لا تعتمد على العرض.
      for (const viewport of [VIEWPORTS[0], VIEWPORTS[2]]) {
        await page.setViewportSize({ width: viewport.width, height: viewport.height });
        for (const [name, path] of [...PUBLIC_ROUTES, ...PRIVATE_ROUTES]) {
          await page.goto(base + path, { waitUntil: 'networkidle' });
          const violations = await axeViolations(page);
          if (violations.length) failures.push(`${lang}/${theme} @${viewport.name} ${name}: ${violations.join(' | ')}`);
        }
      }
      await context.close();

      expect(failures, `\n${failures.join('\n')}\n`).toEqual([]);
    });
  }
}
