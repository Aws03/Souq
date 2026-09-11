import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// client.js يستورد i18n (يلمس localStorage/document عند التحميل) — بديل خفيف يكفي لرسائل الأخطاء.
vi.mock('../i18n', () => ({ default: { exists: () => false, t: (key) => key, language: 'en' } }));

const json = (status, body) => new Response(body === undefined ? null : JSON.stringify(body),
  { status, headers: { 'Content-Type': 'application/json' } });
const problem = (status, code) => json(status, { status, code, title: code });
const user = { id: 7, fullName: 'Test', permissions: [], area: 'Store' };

// خادم وهمي: توكن الوصول الصالح حالياً، وعدد مرّات التجديد، وكل طلب بترويسة توكنه.
function fakeServer({ refreshOk = true } = {}) {
  const state = { valid: 'token-1', refreshes: 0, seen: [] };
  vi.stubGlobal('fetch', vi.fn(async (url, init = {}) => {
    const auth = init.headers?.Authorization;
    state.seen.push(`${url} ${auth ?? '-'}`);
    if (url === '/api/auth/login') {
      return JSON.parse(init.body).password === 'right'
        ? json(200, { accessToken: state.valid, user }) : problem(401, 'InvalidCredentials');
    }
    if (url === '/api/auth/refresh') {
      state.refreshes += 1;
      if (!refreshOk) return problem(401, 'InvalidRefreshToken');
      state.valid = `token-${state.refreshes + 1}`;
      return json(200, { accessToken: state.valid, user });
    }
    return auth === `Bearer ${state.valid}` ? json(200, { items: [] }) : problem(401, 'Unauthenticated');
  }));
  return state;
}

let client;
beforeEach(async () => {
  vi.resetModules();              // حالة الجلسة داخل الوحدة: نسخة جديدة لكل اختبار
  client = await import('./client');
});
afterEach(() => vi.unstubAllGlobals());

const signIn = () => client.api.login({ email: 'a@souq.test', password: 'right' });

describe('session handling', () => {
  it('keeps the access token in memory and sends it with later requests', async () => {
    const server = fakeServer();

    expect(await signIn()).toEqual(user);
    await client.api.getMyOrders();

    expect(server.seen.at(-1)).toBe('/api/orders/mine Bearer token-1');
  });

  it('refreshes once and retries when the access token expires', async () => {
    const server = fakeServer();
    await signIn();
    server.valid = null;   // انتهت صلاحية التوكن على الخادم

    await expect(client.api.getMyOrders()).resolves.toEqual({ items: [] });

    expect(server.refreshes).toBe(1);
    expect(server.seen.at(-1)).toBe('/api/orders/mine Bearer token-2');
  });

  it('shares a single refresh between concurrent requests', async () => {
    const server = fakeServer();
    await signIn();
    server.valid = null;

    await Promise.all([client.api.getMyOrders(), client.api.getOrders(), client.api.getInventory()]);

    expect(server.refreshes).toBe(1);   // تجديدان متوازيان بالرمز نفسه = إعادة استخدام في نظر الخادم
  });

  it('reports an expired session and drops the token when the refresh is rejected', async () => {
    const server = fakeServer({ refreshOk: false });
    await signIn();
    server.valid = null;
    const expired = vi.fn();
    client.authEvents.addEventListener(client.SESSION_EXPIRED, expired);

    await expect(client.api.getMyOrders()).rejects.toMatchObject({ status: 401 });

    expect(expired).toHaveBeenCalledOnce();
    await client.api.getProducts().catch(() => {});
    expect(server.seen.at(-1)).toBe('/api/products -');
  });

  it('never refreshes on a failed sign-in or on requests made without a session', async () => {
    const server = fakeServer();

    await expect(client.api.login({ email: 'a@souq.test', password: 'wrong' }))
      .rejects.toMatchObject({ status: 401, code: 'InvalidCredentials' });
    await expect(client.api.getMyOrders()).rejects.toMatchObject({ status: 401 });

    expect(server.refreshes).toBe(0);
  });

  it('restores a session from the refresh cookie', async () => {
    const server = fakeServer();

    expect(await client.refreshSession()).toEqual(user);
    await client.api.getMyOrders();

    expect(server.seen.at(-1)).toBe('/api/orders/mine Bearer token-2');
  });
});
