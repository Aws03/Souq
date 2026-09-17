// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// سجلّ النشاط. الخادم يرشّح ويرقّم؛ ما يُختبر هنا أن الشاشة:
//   • تُرسل المرشّحات إلى الخادم عند التطبيق وحده — لا مع كل حرف (كل قراءة سطر تدقيق)،
//   • تحفظ التحقيق في الرابط وتنقل الصفحة عبره،
//   • تعرض ما في السطر لا أكثر: فعلٌ مجهول برمزه، وسطر بلا حساب لا يُنسب لأحد، وبيانات مقصوصة كما خُزّنت،
//   • ولا تربط المتجر بصفحته لمن لا يملك تشغيل المتاجر.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
const auth = vi.hoisted(() => ({ permissions: ['platform.audit.view', 'platform.tenants.manage'] }));
vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { id: 1 }, can: (p) => auth.permissions.includes(p) }),
}));
const client = vi.hoisted(() => ({ getPlatformAudit: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const Audit = (await import('./Audit')).default;

const entry = (patch) => ({
  id: 1, occurredAt: '2026-09-17T10:00:00.5Z', area: 'Platform', action: 'tenant.suspended', tenantId: 12,
  actorUserId: 1, actorRole: 'PlatformOwner', targetType: 'Tenant', targetId: '12', metadata: null,
  ipAddress: '10.0.0.1', correlationId: 'abc123', ...patch,
});
const pageOf = (items, { page = 1, totalPages = 1, totalCount = items.length } = {}) => ({ items, page, pageSize: 50, totalCount, totalPages });

let location;
function LocationProbe() { location = useLocation(); return null; }

const renderAt = (url = '/platform/audit') => render(withQueryClient(
  <MemoryRouter initialEntries={[url]}>
    <Routes><Route path="/platform/audit" element={<><Audit /><LocationProbe /></>} /></Routes>
  </MemoryRouter>,
));

beforeEach(() => {
  client.getPlatformAudit.mockReset();
  auth.permissions = ['platform.audit.view', 'platform.tenants.manage'];
});

describe('القراءة', () => {
  it('السطر كما سُجّل: العنوان والرمز، والمتجر برقمه، والفاعل برقمه ودوره', async () => {
    client.getPlatformAudit.mockResolvedValue(pageOf([
      entry({ id: 1 }),
      entry({ id: 2, action: 'something.new-here', tenantId: null, actorUserId: null, area: 'System', actorRole: null }),
    ]));
    renderAt();

    const known = (await screen.findByText('tenant.suspended')).closest('tr');
    expect(within(known).getByText('platform.audit.action.tenant_suspended')).toBeInTheDocument();
    expect(within(known).getByRole('link', { name: /platform\.audit\.storeRef/ })).toHaveAttribute('href', '/platform/stores/12');
    expect(within(known).getByText(/platform\.audit\.actor\.you/)).toBeInTheDocument();

    const unknown = screen.getByText('something.new-here').closest('tr');
    expect(within(unknown).queryByText(/platform\.audit\.action\./)).toBeNull();
    expect(within(unknown).getByText('platform.audit.actor.system')).toBeInTheDocument();
    expect(within(unknown).getByText('platform.audit.noStore')).toBeInTheDocument();

    expect(client.getPlatformAudit).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 50, tenantId: undefined }));
  });

  it('بلا صلاحية تشغيل المتاجر: المتجر رقمٌ لا رابط', async () => {
    auth.permissions = ['platform.audit.view'];
    client.getPlatformAudit.mockResolvedValue(pageOf([entry()]));
    renderAt();
    const row = (await screen.findByText('tenant.suspended')).closest('tr');
    expect(within(row).queryByRole('link')).toBeNull();
    expect(within(row).getByText(/platform\.audit\.storeRef/)).toBeInTheDocument();
  });

  it('التفاصيل: البيانات الوصفية أزواجاً، والمقصوص نصّاً كما خُزّن، واللحظة UTC', async () => {
    client.getPlatformAudit.mockResolvedValue(pageOf([
      entry({ id: 7, metadata: '{"role":"PlatformAdmin"}' }),
      entry({ id: 8, metadata: '{"action":"tenant.cre' }),
    ]));
    renderAt();
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: /platform\.audit\.detailsFor.*7/ }));
    let drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('role')).toBeInTheDocument();
    expect(within(drawer).getByText('PlatformAdmin')).toBeInTheDocument();
    expect(within(drawer).getByText('2026-09-17T10:00:00.5Z')).toBeInTheDocument();
    expect(within(drawer).getByText('abc123')).toBeInTheDocument();
    await user.keyboard('{Escape}');

    await user.click(screen.getByRole('button', { name: /platform\.audit\.detailsFor.*8/ }));
    drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('{"action":"tenant.cre')).toBeInTheDocument();
  });
});

describe('التصفية والترقيم من الخادم', () => {
  it('الكتابة لا تُرسل شيئاً؛ التطبيق يُرسل ويحفظ في الرابط', async () => {
    client.getPlatformAudit.mockResolvedValue(pageOf([entry()]));
    renderAt();
    await screen.findByText('tenant.suspended');
    const user = userEvent.setup();
    expect(client.getPlatformAudit).toHaveBeenCalledTimes(1);

    await user.type(screen.getByLabelText('platform.audit.filter.store'), '12');
    await user.type(screen.getByLabelText('platform.audit.filter.actor'), '3');
    await user.selectOptions(screen.getByLabelText('platform.audit.filter.action'), 'tenant.');
    expect(client.getPlatformAudit).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('button', { name: 'platform.audit.filter.apply' }));
    await waitFor(() => expect(client.getPlatformAudit).toHaveBeenLastCalledWith(
      expect.objectContaining({ tenantId: '12', actorUserId: '3', action: 'tenant.', page: 1 })));
    expect(location.search).toBe('?tenantId=12&action=tenant.&actorUserId=3');
  });

  it('مدّة معكوسة تُقال في مكانها ولا تُرسل', async () => {
    client.getPlatformAudit.mockResolvedValue(pageOf([]));
    renderAt();
    await screen.findByText('platform.audit.emptyTitle');
    const user = userEvent.setup();

    await user.type(screen.getByLabelText('platform.audit.filter.from'), '2026-09-10');
    await user.type(screen.getByLabelText('platform.audit.filter.to'), '2026-09-01');
    await user.click(screen.getByRole('button', { name: 'platform.audit.filter.apply' }));

    expect(screen.getByText('platform.audit.problem.rangeReversed')).toBeInTheDocument();
    expect(client.getPlatformAudit).toHaveBeenCalledTimes(1);
  });

  it('الرابط يفتح التحقيق نفسه، والصفحة التالية من الخادم بالمرشّحات نفسها', async () => {
    client.getPlatformAudit.mockResolvedValue(pageOf([entry()], { totalPages: 3, totalCount: 120 }));
    renderAt('/platform/audit?tenantId=12&page=1');
    await screen.findByText('tenant.suspended');
    expect(screen.getByLabelText('platform.audit.filter.store')).toHaveValue('12');
    expect(screen.getByRole('status')).toHaveTextContent('platform.audit.countFiltered:{"count":120}');

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /common\.next/ }));
    await waitFor(() => expect(client.getPlatformAudit).toHaveBeenLastCalledWith(
      expect.objectContaining({ tenantId: '12', page: 2, pageSize: 50 })));
    expect(location.search).toBe('?tenantId=12&page=2');
  });

  it('من التفاصيل: كل نشاط هذا المتجر', async () => {
    client.getPlatformAudit.mockResolvedValue(pageOf([entry({ id: 5, tenantId: 44 })]));
    renderAt('/platform/audit?action=tenant.');
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: /platform\.audit\.detailsFor/ }));
    await user.click(await screen.findByRole('button', { name: 'platform.audit.showStore' }));
    await waitFor(() => expect(location.search).toBe('?tenantId=44'));
  });
});
