// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// محرّر إعدادات المتجر. ما يُختبر هو ما يكلّف التاجر إن انكسر:
//   • PUT يستبدل الإعدادات كاملةً — فتعديل لون واحد يجب أن يُرسل الروابط وSEO كما هي، لا أن يمحوها.
//   • لوحة غير مقروءة لا تصل الخادم، ويُقال للتاجر أيّ حقل ولماذا.
//   • خطأ الخادم لا يُعرض نجاحاً، والنجاح يُحدِّث هوية اللوحة نفسها.
//   • رفع شعار لا يمحو تعديلاً لم يُحفظ.
//   • العملة للقراءة فقط: قرار المنصّة لا المتجر.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../context/ToastContext', () => ({ useToast: () => toast }));
const tenant = vi.hoisted(() => ({ config: { name: 'Acme' }, refresh: vi.fn() }));
vi.mock('../../app/TenantProvider', () => ({ useTenant: () => tenant }));
vi.mock('../../app/storeTheme', () => ({ loadPreviewFonts: vi.fn() }));

const client = vi.hoisted(() => ({
  getStoreSettings: vi.fn(), getStoreSettingsOptions: vi.fn(), updateStoreSettings: vi.fn(), uploadStoreBranding: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const StoreSettings = (await import('./StoreSettings')).default;

const options = {
  cultures: ['ar', 'en'],
  typography: ['tajawal', 'cairo'],
  themePresets: ['classic', 'minimal'],
  themeModes: ['light', 'dark', 'system'],
  openingStyles: ['doors'],
  socialNetworks: [{ network: 'instagram', domains: ['instagram.com'] }, { network: 'x', domains: ['x.com'] }],
  policyKinds: ['privacy', 'terms', 'returns', 'shipping', 'faq'],
  limits: {
    displayName: 80, announcement: 200, seoTitle: 70, seoDescription: 160, address: 200,
    socialLinks: 8, socialUrl: 300, timeZone: 64, brandingFileBytes: 2097152, policyUrl: 300,
  },
  contrast: { text: 4.5, ui: 3 },
};

const settings = () => ({
  displayName: { ar: 'أكمي', en: 'Acme' },
  locale: { defaultCulture: 'ar', enabledCultures: ['ar', 'en'], timeZone: 'Asia/Amman', currency: 'JOD', currencyDecimals: 3 },
  branding: {
    colors: { primary: '#1E3A5F', secondary: '#E9EEF3', accent: '#F2A541', background: '#FFFFFF', text: '#1F2933', onPrimary: '#FFFFFF', onAccent: '#1F2933' },
    typography: 'tajawal', themePreset: 'classic', themeMode: 'system', opening: { enabled: false, style: 'doors' },
    logoUrl: null, faviconUrl: null, socialImageUrl: null,
  },
  contact: { email: 'hello@acme.test', phone: null, address: {} },
  social: [{ network: 'instagram', url: 'https://instagram.com/acme' }],
  seo: { title: { en: 'Acme — home goods' }, description: {} },
  announcement: { ar: 'شحن مجاني' },
});

const renderPage = async () => {
  render(withQueryClient(<StoreSettings />));
  await screen.findByText('admin.settings.section.identity');
};
const saveButton = () => screen.getByRole('button', { name: 'common.save' });

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  toast.success.mockReset(); tenant.refresh.mockReset();
  client.getStoreSettings.mockResolvedValue(settings());
  client.getStoreSettingsOptions.mockResolvedValue(options);
});

describe('الحفظ', () => {
  it('لا حفظ قبل أن يتغيّر شيء', async () => {
    await renderPage();
    expect(saveButton()).toBeDisabled();
    expect(screen.getByText('admin.settings.upToDate')).toBeInTheDocument();
  });

  it('تعديل لون واحد يُرسل الإعدادات كاملة — الروابط وSEO والإعلان لا تُمحى', async () => {
    client.updateStoreSettings.mockResolvedValue(null);
    await renderPage();
    const user = userEvent.setup();

    const primary = screen.getByLabelText('admin.settings.colors.primary');
    await user.clear(primary);
    await user.type(primary, '#0B5D3B');
    expect(screen.getByText('admin.settings.unsaved')).toBeInTheDocument();
    await user.click(saveButton());

    await waitFor(() => expect(client.updateStoreSettings).toHaveBeenCalledTimes(1));
    const body = client.updateStoreSettings.mock.calls[0][0];
    expect(body.branding.colors.primary).toBe('#0B5D3B');
    expect(body.social).toEqual([{ network: 'instagram', url: 'https://instagram.com/acme' }]);
    expect(body.seo.title.en).toBe('Acme — home goods');
    expect(body.announcement.ar).toBe('شحن مجاني');
    expect(body.locale).toEqual({ defaultCulture: 'ar', enabledCultures: ['ar', 'en'], timeZone: 'Asia/Amman' });
    expect(body).not.toHaveProperty('locale.currency');

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('admin.settings.saved'));
    expect(tenant.refresh).toHaveBeenCalled();
  });

  it('لوحة غير مقروءة لا تصل الخادم، والخطأ تحت الحقل الذي سبّبه', async () => {
    await renderPage();
    const user = userEvent.setup();

    const text = screen.getByLabelText('admin.settings.colors.text');
    await user.clear(text);
    await user.type(text, '#CCCCCC');
    await user.click(saveButton());

    expect(client.updateStoreSettings).not.toHaveBeenCalled();
    expect(text).toHaveAttribute('aria-invalid', 'true');
    expect(document.activeElement).toBe(text);
    expect(screen.getByText(/^admin\.settings\.problem\.contrast\.text:/)).toBeInTheDocument();
  });

  it('فحوص القراءة تظهر حيّةً قبل الحفظ', async () => {
    await renderPage();
    const list = screen.getByRole('list', { name: 'admin.settings.contrast.title' });
    expect(within(list).getAllByText('admin.settings.contrast.pass')).toHaveLength(4);

    const user = userEvent.setup();
    const accent = screen.getByLabelText('admin.settings.colors.accent');
    await user.clear(accent);
    await user.type(accent, '#808080');   // رماديّ متوسّط: لا الأبيض ولا نصّ المتجر يبلغ 4.5:1 عليه
    // اللون ناقص أثناء الكتابة فلا فحص له؛ القائمة تعود حين يكتمل — فتُقرأ من جديد.
    const updated = screen.getByRole('list', { name: 'admin.settings.contrast.title' });
    expect(within(updated).getByText('admin.settings.contrast.fail')).toBeInTheDocument();
  });

  it('رفض الخادم يُعرض خطأً ولا يُقال "حُفظ"', async () => {
    client.updateStoreSettings.mockRejectedValue(new Error('server said no'));
    await renderPage();
    const user = userEvent.setup();

    await user.click(screen.getByLabelText('admin.settings.themeMode.dark'));
    await user.click(saveButton());

    expect(await screen.findByText('server said no')).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
    expect(tenant.refresh).not.toHaveBeenCalled();
    expect(screen.getByText('admin.settings.unsaved')).toBeInTheDocument();
  });

  it('تجاهل التعديلات يعيد المحفوظ', async () => {
    await renderPage();
    const user = userEvent.setup();
    const email = screen.getByLabelText('admin.settings.contactEmail');
    await user.clear(email);
    await user.type(email, 'other@acme.test');
    await user.click(screen.getByRole('button', { name: 'admin.settings.discard' }));
    expect(email).toHaveValue('hello@acme.test');
  });
});

describe('ما ليس للمتجر أن يعدّله', () => {
  it('العملة معروضة للقراءة، بلا حقل', async () => {
    await renderPage();
    expect(screen.getByText('JOD')).toBeInTheDocument();
    expect(screen.queryByLabelText('admin.settings.currency')).toBeNull();
  });

  it('اللغة الافتراضية الجديدة تُفعَّل معها بدل أن يُرفض الحفظ', async () => {
    await renderPage();
    const user = userEvent.setup();
    await user.click(screen.getByLabelText('admin.settings.culture.en', { selector: 'input[type="checkbox"]' }));
    await user.selectOptions(screen.getByLabelText('admin.settings.defaultCulture'), 'en');
    expect(screen.getByLabelText('admin.settings.culture.en', { selector: 'input[type="checkbox"]' })).toBeChecked();
  });
});

describe('الملفات', () => {
  it('رفع شعار لا يمحو تعديلاً لم يُحفظ', async () => {
    client.uploadStoreBranding.mockResolvedValue({ url: '/uploads/tenants/9/branding/logo.png' });
    await renderPage();
    const user = userEvent.setup();

    const email = screen.getByLabelText('admin.settings.contactEmail');
    await user.clear(email);
    await user.type(email, 'sales@acme.test');

    const file = new File(['png'], 'logo.png', { type: 'image/png' });
    await user.upload(screen.getByLabelText('admin.settings.assets.logo.label'), file);

    await waitFor(() => expect(client.uploadStoreBranding).toHaveBeenCalledWith('logo', file));
    expect(await screen.findByAltText('admin.settings.assets.logo.alt')).toHaveAttribute('src', '/uploads/tenants/9/branding/logo.png');
    expect(email).toHaveValue('sales@acme.test');
    expect(screen.getByText('admin.settings.unsaved')).toBeInTheDocument();
  });

  it('ملف أكبر من حدّ الخادم لا يُرفع', async () => {
    client.getStoreSettingsOptions.mockResolvedValue({ ...options, limits: { ...options.limits, brandingFileBytes: 2 } });
    await renderPage();
    const user = userEvent.setup();

    await user.upload(screen.getByLabelText('admin.settings.assets.favicon.label'),
      new File(['too big'], 'favicon.png', { type: 'image/png' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('admin.settings.assets.tooLarge');
    expect(client.uploadStoreBranding).not.toHaveBeenCalled();
  });
});
