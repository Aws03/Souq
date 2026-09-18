import { expect, test } from '@playwright/test';

// ============================================================================
// المتجر الثاني — النصف الآخر من معيار خروج المرحلة ("الرحلات الحرجة تمرّ لمتجرين").
//
// نفس البناء، مضيف آخر: second.localhost:5173. المتجر يُحلّ من المضيف على الخادم، ووكيل
// Vite يمرّر ترويسة Host كما هي — فهذا فصل حقيقي لا محاكاة.
//
// ما يُختبر هنا ليس تكرار رحلة الشراء (نفس الشيفرة)، بل ما يختلف فعلاً بين متجرين: الهوية،
// والعملة، والكتالوج، وأن أحدهما لا يرى بيانات الآخر إطلاقاً.
// ============================================================================
// المنفذ يُشتقّ من عنوان التشغيل لا يُكتب: كان `:5173` ثابتاً، فكانت هذه الرحلات تفشل بحكم
// بنائها على أي حزمة أخرى. ويبقى الشرط الحقيقي قائماً: `second.localhost` لا يُحلّ إلا حيث
// تُسمح دقّة التطوير (Development/Testing) — في الإنتاج يُحلّ المتجر من نطاقٍ مسجَّل ومُتحقَّق
// منه، وهذا سلوكٌ صحيح لا قيد. والمتجر نفسه يُجهَّز بـ `scripts/qa-second-store.py`.
const PORT = new URL(process.env.SOUQ_E2E_BASE_URL || 'http://localhost:5173').port;
const SECOND = `http://second.localhost${PORT ? `:${PORT}` : ''}`;

test.use({ baseURL: SECOND });

test('the second store renders its own identity, not the first store\'s', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Second Store' })).toBeVisible();
  expect(await page.title()).toContain('Second Store');

  // لا أثر لهوية المتجر الأول في أي مكان من الصفحة.
  const body = await page.locator('body').textContent();
  expect(body).not.toContain('Marka');
  expect(body).not.toContain('JOD');
});

test('prices are in the second store\'s own currency', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('article').first()).toBeVisible();
  await expect(page.locator('article').first()).toContainText('USD');
});

test('the catalog contains only the second store\'s products', async ({ page, request }) => {
  const res = await request.get(`${SECOND}/api/products?page=1&pageSize=50`);
  const body = await res.json();
  expect(body.totalCount).toBe(3);
  expect(body.items.every((p) => p.slug.startsWith('second-widget'))).toBe(true);

  await page.goto('/');
  await expect(page.locator('article').filter({ hasText: 'Second Widget' }).first()).toBeVisible();
  // ولا منتج من المتجر الأول في أي صفّ.
  await expect(page.locator('article').filter({ hasText: 'Thermal Mug' })).toHaveCount(0);
});

test('the first store\'s product is not found here', async ({ page, request }) => {
  // المنتج موجود على المضيف الأول، وغير موجود على الثاني — 404 لا 403: لا يُفشى وجوده.
  const first = await (await request.get('http://localhost:5173/api/products?page=1&pageSize=1')).json();
  const slug = first.items[0].slug;

  const cross = await request.get(`${SECOND}/api/products/by-slug/${slug}`);
  expect(cross.status()).toBe(404);

  await page.goto(`/products/${slug}`);
  await expect(page.getByRole('alert')).toBeVisible();
});

test('the second store serves the same robots.txt rules', async ({ request }) => {
  const res = await request.get(`${SECOND}/robots.txt`);
  expect(res.status()).toBe(200);
  expect(await res.text()).toContain('Disallow: /checkout');
});

test('a customer of the second store is unknown to the first', async ({ request }) => {
  const email = `tenant2+${Date.now()}@souq.test`;
  const register = await request.post(`${SECOND}/api/auth/register`, {
    data: { fullName: 'Second Customer', email, password: 'Customer@12345' },
  });
  expect(register.ok()).toBeTruthy();

  // نفس البريد وكلمة السر على المتجر الأول: حساب لا وجود له هناك.
  const crossLogin = await request.post('http://localhost:5173/api/auth/login', {
    data: { email, password: 'Customer@12345' },
  });
  expect(crossLogin.status()).toBe(401);
});
