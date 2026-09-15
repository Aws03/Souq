import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// client.js يستورد i18n (يلمس localStorage/document عند التحميل) — بديل خفيف يكفي لرسائل الأخطاء.
vi.mock('../i18n', () => ({ default: { exists: () => false, t: (key) => key, language: 'en' } }));

const json = (status, body) => new Response(body === undefined ? null : JSON.stringify(body),
  { status, headers: { 'Content-Type': 'application/json' } });
const problem = (status, code) => json(status, { status, code, title: code });
const user = { id: 7, fullName: 'Test', permissions: [], area: 'Store' };

// خادم وهمي: توكن الوصول الصالح حالياً، وعدد مرّات التجديد، وكل طلب بترويسة توكنه.
function fakeServer({ refreshOk = true, refreshStatus = 401 } = {}) {
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
      if (!refreshOk) return problem(refreshStatus, refreshStatus === 429 ? 'TooManyRequests' : 'InvalidRefreshToken');
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

    expect(await client.refreshSession()).toEqual({ user, outcome: 'active' });
    await client.api.getMyOrders();

    expect(server.seen.at(-1)).toBe('/api/orders/mine Bearer token-2');
  });
});

// ============================================================================
// "فشل التجديد" ليس شيئاً واحداً (المرحلة 16 §22): 401 انتهاء مؤكّد، و429/5xx/شبكة ظرفٌ
// عابر. خلطُهما كان يُخرج زبوناً خلف عنوان مشترك من جلسته — وربما في منتصف الدفع.
// ============================================================================
describe('a refresh that fails for a transient reason', () => {
  it('429 لا تُنهي الجلسة — النتيجة "غير معروف" لا "منتهية"', async () => {
    fakeServer({ refreshOk: false, refreshStatus: 429 });

    expect(await client.refreshSession()).toEqual({ user: null, outcome: client.REFRESH_UNKNOWN });
  });

  it('عطل خادم لا يُنهي الجلسة', async () => {
    fakeServer({ refreshOk: false, refreshStatus: 503 });

    expect((await client.refreshSession()).outcome).toBe(client.REFRESH_UNKNOWN);
  });

  it('شبكة مقطوعة لا تُنهي الجلسة', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    expect((await client.refreshSession()).outcome).toBe(client.REFRESH_UNKNOWN);
  });

  it('401 وحدها تُنهي الجلسة', async () => {
    fakeServer({ refreshOk: false, refreshStatus: 401 });

    expect((await client.refreshSession()).outcome).toBe(client.REFRESH_EXPIRED);
  });

  it('حدّ معدّل على تجديدٍ بعد 401 لا يُطلق حدث انتهاء الجلسة', async () => {
    // الحالة الحقيقية: طلبٌ يحمل توكناً منتهياً، وتجديدُه يُرفض بحدّ معدّل. الواجهة كانت
    // تعلن انتهاء الجلسة وتُخرج الزبون؛ الصحيح أن تُبقيها وتُعيد المحاولة لاحقاً.
    fakeServer();
    await signIn();
    let expired = 0;
    client.authEvents.addEventListener(client.SESSION_EXPIRED, () => { expired += 1; });

    vi.stubGlobal('fetch', vi.fn(async (url) => (
      url === '/api/auth/refresh' ? problem(429, 'TooManyRequests') : problem(401, 'Unauthenticated'))));

    await expect(client.api.getMyOrders()).rejects.toThrow();
    expect(expired).toBe(0);
  });

  it('401 على التجديد يُطلق الحدث كما كان', async () => {
    fakeServer();
    await signIn();
    let expired = 0;
    client.authEvents.addEventListener(client.SESSION_EXPIRED, () => { expired += 1; });

    vi.stubGlobal('fetch', vi.fn(async (url) => (
      url === '/api/auth/refresh' ? problem(401, 'InvalidRefreshToken') : problem(401, 'Unauthenticated'))));

    await expect(client.api.getMyOrders()).rejects.toThrow();
    expect(expired).toBe(1);
  });
});
