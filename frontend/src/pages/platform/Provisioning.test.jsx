// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// شاشات تجهيز المتاجر. ما يُختبر هو ما يكلّف المنصّة إن انكسر:
//   • القائمة تقول ما ينقص كل متجر وتستأنف تجهيزه من أوّل ما ينقصه.
//   • الإنشاء لا يُرسل هويةً يرفضها الخادم، ومعرّفٌ مأخوذ يُقال بجانب حقله.
//   • الأرشفة (نهائية) لا تحدث بنقرة: تتطلّب كتابة المعرّف.
//   • دعوة المدير لا تُعرض قبل النطاق — رابطها يُفتح على نطاق المتجر.
//   • رفض الخادم يُقرأ في مكانه، والنجاح يعيد القراءة من الخادم.
//   • محرّر الإعدادات من المنصّة يكتب ويرفع إلى نقاط المتجر المحدّد، لا نقاط متجر المضيف.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../context/ToastContext', () => ({ useToast: () => toast }));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => ({ can: () => true }) }));
vi.mock('../../i18n', () => ({ formatDate: (v) => String(v).slice(0, 10) }));
vi.mock('../../app/storeTheme', () => ({ loadPreviewFonts: vi.fn() }));

const client = vi.hoisted(() => ({
  getProvisioningOptions: vi.fn(), getPlatformStores: vi.fn(), getPlatformStore: vi.fn(), createPlatformStore: vi.fn(),
  getPlatformStoreAccounts: vi.fn(), changePlatformStoreStatus: vi.fn(), addPlatformStoreDomain: vi.fn(),
  removePlatformStoreDomain: vi.fn(), setPlatformStorePrimaryDomain: vi.fn(), verifyPlatformStoreDomain: vi.fn(),
  invitePlatformStoreAdmin: vi.fn(), setPlatformStoreModules: vi.fn(), updatePlatformStore: vi.fn(),
  updatePlatformStoreSettings: vi.fn(), uploadPlatformStoreBranding: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const Stores = (await import('./Stores')).default;
const NewStore = (await import('./NewStore')).default;
const StoreDetail = (await import('./StoreDetail')).default;
const StoreSettingsPage = (await import('./StoreSettingsPage')).default;

const settingsOptions = {
  cultures: ['ar', 'en'], typography: ['tajawal'], themePresets: ['classic'], themeModes: ['light', 'dark', 'system'],
  openingStyles: ['doors'], socialNetworks: [{ network: 'instagram', domains: ['instagram.com'] }],
  policyKinds: ['privacy', 'terms', 'returns', 'shipping', 'faq'],
  limits: { displayName: 80, announcement: 200, seoTitle: 70, seoDescription: 160, address: 200, socialLinks: 8, socialUrl: 300, timeZone: 64, brandingFileBytes: 2097152, policyUrl: 300 },
  contrast: { text: 4.5, ui: 3 },
};
const options = {
  settings: settingsOptions, modules: ['promotions', 'reviews', 'wishlist'],
  statuses: ['Provisioning', 'Active', 'Suspended', 'Archived'], reservedSlugs: ['admin', 'api'],
  limits: { nameMin: 2, nameMax: 100, slugMin: 2, slugMax: 40, timeZoneMax: 64, hostMax: 253, fullNameMax: 150, emailMax: 256 },
};
const settings = {
  displayName: { en: 'Acme' }, locale: { defaultCulture: 'en', enabledCultures: ['en'], timeZone: 'UTC', currency: 'USD', currencyDecimals: 2 },
  branding: { colors: { primary: '#1E3A5F', secondary: '#E9EEF3', accent: '#F2A541', background: '#FFFFFF', text: '#1F2933', onPrimary: '#FFFFFF', onAccent: '#1F2933' },
    typography: 'tajawal', themePreset: 'classic', themeMode: 'system', opening: { enabled: false, style: 'doors' }, logoUrl: null, faviconUrl: null, socialImageUrl: null },
  contact: { email: null, phone: null, address: {} }, social: [], seo: { title: {}, description: {} }, announcement: {},
};
const store = (patch = {}) => ({
  id: 7, name: 'Acme', slug: 'acme', status: 'Provisioning', currency: 'USD', defaultCulture: 'en', timeZone: 'UTC',
  createdAt: '2026-09-17T08:00:00Z', domains: [], modules: ['promotions', 'reviews', 'wishlist'], settings,
  // C1: ما يسري فعلاً، وخطّته. الافتراضي هنا "الخطة التأسيسية تمنح الثلاث" كما في متجر حقيقي جديد.
  effectiveModules: ['promotions', 'reviews', 'wishlist'], planCode: 'foundation', planName: 'الخطة التأسيسية', ...patch,
});
const page = (items) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname}</output>;
}

const renderAt = (path, element, routePath) => render(withQueryClient(
  <MemoryRouter initialEntries={[path]}>
    <Routes>
      <Route path={routePath} element={element} />
      <Route path="*" element={<Where />} />
    </Routes>
  </MemoryRouter>,
));

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  toast.success.mockReset();
  client.getProvisioningOptions.mockResolvedValue(options);
  client.getPlatformStoreAccounts.mockResolvedValue(page([]));
});

describe('قائمة المتاجر', () => {
  it('تقول ما ينقص كل متجر، وتستأنف التجهيز من أوّل ما ينقصه', async () => {
    client.getPlatformStores.mockResolvedValue(page([
      { id: 1, name: 'Bare', slug: 'bare', status: 'Provisioning', currency: 'USD', defaultCulture: 'en', primaryHost: null, domainCount: 0, createdAt: '2026-09-01', activeAdmins: 0, pendingAdminInvitations: 0 },
      { id: 2, name: 'Hosted', slug: 'hosted', status: 'Provisioning', currency: 'USD', defaultCulture: 'en', primaryHost: 'hosted.test', domainCount: 1, createdAt: '2026-09-02', activeAdmins: 0, pendingAdminInvitations: 0 },
      { id: 3, name: 'Live', slug: 'live', status: 'Active', currency: 'USD', defaultCulture: 'en', primaryHost: 'live.test', domainCount: 1, createdAt: '2026-09-03', activeAdmins: 1, pendingAdminInvitations: 0 },
    ]));
    renderAt('/platform/stores', <Stores />, '/platform/stores');

    const bare = (await screen.findByText('Bare')).closest('tr');
    expect(within(bare).getByText('platform.stores.noDomain')).toBeInTheDocument();
    expect(within(bare).getByRole('link', { name: 'platform.stores.continueSetup' })).toHaveAttribute('href', '/platform/stores/1/setup/domains');
    expect(within(screen.getByText('Hosted').closest('tr')).getByRole('link', { name: 'platform.stores.continueSetup' }))
      .toHaveAttribute('href', '/platform/stores/2/setup/admin');
    expect(within(screen.getByText('Live').closest('tr')).getByRole('link', { name: 'platform.stores.manage' }))
      .toHaveAttribute('href', '/platform/stores/3');
  });

  it('البحث والحالة يُرسلان للخادم، والصفحة تعود للأولى', async () => {
    client.getPlatformStores.mockResolvedValue(page([]));
    renderAt('/platform/stores', <Stores />, '/platform/stores');
    await screen.findByText('platform.stores.emptyTitle');
    const user = userEvent.setup();

    await user.selectOptions(await screen.findByLabelText('platform.stores.statusLabel'), 'Suspended');
    await waitFor(() => expect(client.getPlatformStores).toHaveBeenLastCalledWith(
      { search: undefined, status: 'Suspended', page: 1, pageSize: 20 }));
    await user.type(screen.getByLabelText('platform.stores.searchLabel'), 'acme');
    await waitFor(() => expect(client.getPlatformStores).toHaveBeenLastCalledWith(
      { search: 'acme', status: 'Suspended', page: 1, pageSize: 20 }), { timeout: 2000 });
    expect(await screen.findByText('platform.stores.noMatchTitle')).toBeInTheDocument();
  });
});

describe('إنشاء متجر', () => {
  const fill = async (user, { name = 'Acme Home', currency = 'usd' } = {}) => {
    await user.type(await screen.findByLabelText('platform.identity.name'), name);
    if (currency) await user.type(screen.getByLabelText('platform.identity.currency'), currency);
  };

  it('المعرّف يُقترح من الاسم، والإنشاء ينتقل إلى خطوة المظهر للمتجر الجديد', async () => {
    client.createPlatformStore.mockResolvedValue({ id: 42 });
    renderAt('/platform/stores/new', <NewStore />, '/platform/stores/new');
    const user = userEvent.setup();

    await fill(user);
    expect(screen.getByLabelText('platform.identity.slug')).toHaveValue('acme-home');
    await user.click(screen.getByRole('button', { name: 'platform.identity.create' }));

    await waitFor(() => expect(client.createPlatformStore).toHaveBeenCalledWith(
      { name: 'Acme Home', slug: 'acme-home', currency: 'USD', defaultCulture: 'ar', timeZone: 'UTC' }));
    expect(await screen.findByTestId('where')).toHaveTextContent('/platform/stores/42/setup/branding');
  });

  it('هوية ناقصة لا تُرسل، والتركيز على أوّل حقل خاطئ', async () => {
    renderAt('/platform/stores/new', <NewStore />, '/platform/stores/new');
    const user = userEvent.setup();

    await fill(user, { name: 'Acme', currency: '' });
    await user.clear(screen.getByLabelText('platform.identity.slug'));
    await user.type(screen.getByLabelText('platform.identity.slug'), 'admin');
    await user.click(screen.getByRole('button', { name: 'platform.identity.create' }));

    expect(client.createPlatformStore).not.toHaveBeenCalled();
    expect(screen.getByLabelText('platform.identity.slug')).toHaveFocus();
    expect(screen.getByText(/platform\.problem\.slugReserved/)).toBeInTheDocument();
    expect(screen.getByText('platform.problem.currencyInvalid')).toBeInTheDocument();
  });

  it('معرّف مأخوذ على الخادم يُقال بجانب المعرّف، لا لافتة عامّة', async () => {
    client.createPlatformStore.mockRejectedValue(Object.assign(new Error('taken'), { code: 'TenantSlugTaken' }));
    renderAt('/platform/stores/new', <NewStore />, '/platform/stores/new');
    const user = userEvent.setup();

    await fill(user);
    await user.click(screen.getByRole('button', { name: 'platform.identity.create' }));

    expect(await screen.findByText('platform.problem.slugTaken')).toBeInTheDocument();
    expect(screen.getByLabelText('platform.identity.slug')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByText('taken')).toBeNull();
  });
});

describe('صفحة المتجر', () => {
  const renderDetail = () => renderAt('/platform/stores/7', <StoreDetail />, '/platform/stores/:id');

  it('متجر بلا نطاق: الجاهزية تقول ذلك، ودعوة المدير غير معروضة قبل النطاق', async () => {
    client.getPlatformStore.mockResolvedValue(store());
    renderDetail();

    expect(await screen.findByText(/^platform\.readiness\.domain\.missing/)).toBeInTheDocument();
    expect(screen.getByText('platform.admins.needsDomain')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'platform.admins.invite' })).toBeNull();
    expect(screen.getByRole('link', { name: 'platform.stores.continueSetup' })).toHaveAttribute('href', '/platform/stores/7/setup/domains');
  });

  it('نطاق غير صالح لا يُرسل؛ ورفض الخادم يُقرأ في القسم نفسه', async () => {
    client.getPlatformStore.mockResolvedValue(store());
    client.addPlatformStoreDomain.mockRejectedValue(new Error('This host is the platform\'s own.'));
    renderDetail();
    const user = userEvent.setup();

    const input = await screen.findByLabelText('platform.domains.addLabel');
    await user.type(input, 'shop.test:5173');
    await user.click(screen.getByRole('button', { name: 'platform.domains.add' }));
    expect(client.addPlatformStoreDomain).not.toHaveBeenCalled();
    expect(screen.getByText('platform.problem.hostNoPort')).toBeInTheDocument();

    await user.clear(input);
    await user.type(input, 'Admin.Localhost');
    await user.click(screen.getByRole('button', { name: 'platform.domains.add' }));
    expect(client.addPlatformStoreDomain).toHaveBeenCalledWith(7, 'admin.localhost');
    expect(await screen.findByText('This host is the platform\'s own.')).toBeInTheDocument();
  });

  it('الأرشفة لا تحدث بنقرة: تتطلّب كتابة المعرّف، ثم يُعاد قراءة المتجر', async () => {
    client.getPlatformStore.mockResolvedValue(store({ status: 'Active', domains: [{ host: 'acme.test', isPrimary: true, verifiedAt: null }] }));
    client.changePlatformStoreStatus.mockResolvedValue(null);
    renderDetail();
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'platform.lifecycle.action.Archive' }));
    const dialog = screen.getByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'platform.lifecycle.action.Archive' });
    expect(confirm).toBeDisabled();

    await user.type(within(dialog).getByRole('textbox'), 'acme');
    await user.click(confirm);

    await waitFor(() => expect(client.changePlatformStoreStatus).toHaveBeenCalledWith(7, 'Archive'));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(client.getPlatformStore.mock.calls.length).toBeGreaterThan(1);
  });

  it('تفعيل متجر بلا مدير يُنبَّه إليه داخل التأكيد، ولا يُمنع', async () => {
    client.getPlatformStore.mockResolvedValue(store({ domains: [{ host: 'acme.test', isPrimary: true, verifiedAt: null }] }));
    client.changePlatformStoreStatus.mockResolvedValue(null);
    renderDetail();
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'platform.lifecycle.action.Activate' }));
    const dialog = screen.getByRole('alertdialog');
    expect(within(dialog).getByText('platform.lifecycle.warning.admin')).toBeInTheDocument();
    expect(within(dialog).queryByText('platform.lifecycle.warning.domain')).toBeNull();

    await user.click(within(dialog).getByRole('button', { name: 'platform.lifecycle.action.Activate' }));
    await waitFor(() => expect(client.changePlatformStoreStatus).toHaveBeenCalledWith(7, 'Activate'));
  });

  it('رفض الخادم لتغيير الحالة يبقى داخل الحوار', async () => {
    client.getPlatformStore.mockResolvedValue(store({ status: 'Active' }));
    client.changePlatformStoreStatus.mockRejectedValue(new Error('Only an active store can be suspended'));
    renderDetail();
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'platform.lifecycle.action.Suspend' }));
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'platform.lifecycle.action.Suspend' }));
    expect(await within(screen.getByRole('alertdialog')).findByRole('alert')).toHaveTextContent('Only an active store can be suspended');
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('المدير يُدعى بعد النطاق، بالبريد مقصوصاً صغيراً', async () => {
    client.getPlatformStore.mockResolvedValue(store({ domains: [{ host: 'acme.test', isPrimary: true, verifiedAt: null }] }));
    client.invitePlatformStoreAdmin.mockResolvedValue({ userId: 3, renewed: false });
    renderDetail();
    const user = userEvent.setup();

    const section = (await screen.findByRole('heading', { name: 'platform.admins.title' })).closest('section');
    await user.type(within(section).getByLabelText('admin.staff.nameLabel'), 'Client Boss');
    await user.type(within(section).getByLabelText('admin.staff.emailLabel'), ' Boss@Client.TEST ');
    await user.click(within(section).getByRole('button', { name: 'platform.admins.invite' }));

    await waitFor(() => expect(client.invitePlatformStoreAdmin).toHaveBeenCalledWith(7, { fullName: 'Client Boss', email: 'boss@client.test' }));
  });

  it('المؤرشف بلا إجراءات حالة', async () => {
    client.getPlatformStore.mockResolvedValue(store({ status: 'Archived' }));
    renderDetail();
    await screen.findByText('platform.lifecycle.current.Archived');
    for (const action of ['Activate', 'Suspend', 'Archive']) {
      expect(screen.queryByRole('button', { name: `platform.lifecycle.action.${action}` })).toBeNull();
    }
  });
});

describe('محرّر الإعدادات من المنصّة', () => {
  it('يكتب ويرفع إلى نقاط المتجر المحدّد', async () => {
    client.getPlatformStore.mockResolvedValue(store());
    client.updatePlatformStoreSettings.mockResolvedValue(null);
    client.uploadPlatformStoreBranding.mockResolvedValue({ url: '/uploads/tenants/7/branding/logo.png' });
    renderAt('/platform/stores/7/settings', <StoreSettingsPage />, '/platform/stores/:id/settings');
    const user = userEvent.setup();

    await user.click(await screen.findByLabelText('admin.settings.themeMode.dark'));
    await user.click(screen.getByRole('button', { name: 'common.save' }));
    await waitFor(() => expect(client.updatePlatformStoreSettings).toHaveBeenCalledWith(7,
      expect.objectContaining({ branding: expect.objectContaining({ themeMode: 'dark' }) })));

    const file = new File(['png'], 'logo.png', { type: 'image/png' });
    await user.upload(screen.getByLabelText('admin.settings.assets.logo.label'), file);
    await waitFor(() => expect(client.uploadPlatformStoreBranding).toHaveBeenCalledWith(7, 'logo', file));
  });
});

// ── الخطة والوحدات السارية (C1، ADR-0053) ────────────────────────────────────
describe('خطة المتجر وما تمنحه', () => {
  it('الوحدة المفعّلة التي لا تسري يُقال سببها، ويبقى نزع تأشيرها ممكناً', async () => {
    // مربّعٌ مؤشَّر لوحدةٍ لا تعمل، بلا سبب معروض، كان يبدو عطباً في الوحدة نفسها.
    client.getPlatformStore.mockResolvedValue(store({
      modules: ['promotions', 'reviews'], effectiveModules: ['promotions'], planName: 'خطة محدودة',
    }));
    client.getPlatformStoreAccounts.mockResolvedValue(page([]));
    client.getProvisioningOptions.mockResolvedValue(options);
    renderAt('/platform/stores/7', <StoreDetail />, '/platform/stores/:id');

    await screen.findByText('خطة محدودة');
    expect(await screen.findAllByText('platform.modules.planLocked')).toHaveLength(1);   // reviews وحدها
    // لا مربّع معطّل: المطفأة بالمفتاح (wishlist) لا يُعرف عنها شيء، والمفعّلة يجب أن تبقى قابلة للنزع.
    expect(screen.getAllByRole('checkbox').filter((b) => b.disabled)).toHaveLength(0);
  });

  it('متجر بلا خطة يُقال عنه ذلك صراحةً بدل أن تبدو وحداته معطوبة', async () => {
    client.getPlatformStore.mockResolvedValue(store({ effectiveModules: [], planCode: null, planName: null }));
    client.getPlatformStoreAccounts.mockResolvedValue(page([]));
    client.getProvisioningOptions.mockResolvedValue(options);
    renderAt('/platform/stores/7', <StoreDetail />, '/platform/stores/:id');

    expect(await screen.findByText('platform.store.noPlan')).toBeInTheDocument();
    // الثلاث مفعّلة بالمفتاح ولا تسري واحدة: السبب معروض على كلٍّ منها.
    expect(await screen.findAllByText('platform.modules.planLocked')).toHaveLength(3);
  });
});
