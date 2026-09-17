// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { withQueryClient } from '../../test/queryWrapper';
import { expectNoViolations } from '../../test/axe';

// ============================================================================
// صفحة الخيارات والمتغيّرات (ADR-0040). الخادم يحرس القواعد؛ ما يُختبر هنا أن الصفحة:
//   • تعرض المتغيّرات بأوصافها من قيم الخيارات والافتراضي مميّزاً، وتنبّه حين يُخفي تعدّد النشط المنتجَ من الواجهة،
//   • لا تعرض حذفاً فعّالاً لقيمة يستخدمها متغيّر، وتفحص النموذج بالحدود المنشورة قبل الإرسال،
//   • ترسل التعريف كما يقبله الخادم وتعرض رفضه كما هو،
//   • تنشئ التركيبات المختارة وحدها، وتؤكّد قبل تعطيل متغيّر ولا تعرض تعطيل الافتراضي،
//   • وتجتاز فحص الإتاحة البنيوي.
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
vi.mock('../../app/TenantProvider', () => ({ useTenant: () => ({ config: { settings: { locale: { defaultCulture: 'ar' } } } }) }));
vi.mock('../../i18n', () => ({ formatDate: (v) => v, default: { language: 'en' } }));
vi.mock('../../components/product/ProductBadges', () => ({ formatPrice: (amount, currency) => `${amount} ${currency}` }));

const client = vi.hoisted(() => ({
  getAdminProduct: vi.fn(), setProductOptions: vi.fn(), createProductVariants: vi.fn(), updateProductVariant: vi.fn(),
  setProductVariantStatus: vi.fn(), setDefaultProductVariant: vi.fn(),
  adjustVariantStock: vi.fn(), setVariantStockThreshold: vi.fn(), getVariantStockMovements: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const ProductVariants = (await import('./ProductVariants')).default;

const limits = { maxOptions: 3, maxValuesPerOption: 20, maxVariants: 100, nameMaxLength: 50, skuMaxLength: 64 };
const variant = (patch) => ({
  isDefault: false, isActive: true, sku: null, price: 20, compareAtPrice: null, onHand: 4, reserved: 1, available: 3,
  lowStockThreshold: 5, ...patch,
});
const product = (patch = {}) => ({
  id: 7, slug: 'tee', status: 'Active', price: 20, currency: 'JOD', translations: { en: { name: 'Tee' }, ar: { name: 'قميص' } },
  options: [{ id: 1, position: 0, names: { ar: 'المقاس', en: 'Size' }, values: [
    { id: 10, position: 0, names: { ar: 'S', en: 'S' } },
    { id: 11, position: 1, names: { ar: 'M', en: 'M' } },
    { id: 12, position: 2, names: { ar: 'L', en: 'L' } },
  ] }],
  variants: [variant({ id: 70, isDefault: true, optionValueIds: [10], sku: 'TEE-S' }), variant({ id: 71, optionValueIds: [11], isActive: false })],
  variantLimits: limits,
  ...patch,
});

const renderPage = () => render(withQueryClient(
  <MemoryRouter initialEntries={['/admin/products/7/variants']}>
    <Routes><Route path="/admin/products/:productId/variants" element={<ProductVariants />} /></Routes>
  </MemoryRouter>,
));

const variantsTable = () => screen.getByRole('table');
const row = (label) => within(variantsTable()).getByText(label).closest('tr');

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  toast.success.mockReset(); toast.error.mockReset();
  client.getAdminProduct.mockResolvedValue(product());
});

describe('المتغيّرات', () => {
  it('تعرض كل متغيّر بوصفه وحالته، والافتراضي مميّزاً، بلا تنبيه ما دام نشط واحد', async () => {
    renderPage();
    await screen.findByRole('table');

    expect(client.getAdminProduct).toHaveBeenCalledWith('7');
    expect(within(row('S')).getByText('admin.variants.default')).toBeInTheDocument();
    expect(within(row('S')).getByText('TEE-S')).toBeInTheDocument();
    expect(within(row('M')).getByText('admin.variants.inactive')).toBeInTheDocument();
    expect(screen.queryByText('admin.variants.storefrontHidden')).toBeNull();
  });

  it('تنبّه أن الواجهة تخفي المنتج حين يكون أكثر من متغيّر نشطاً', async () => {
    client.getAdminProduct.mockResolvedValue(product({
      variants: [variant({ id: 70, isDefault: true, optionValueIds: [10] }), variant({ id: 71, optionValueIds: [11] })],
    }));
    renderPage();

    expect(await screen.findByText('admin.variants.storefrontHidden')).toBeInTheDocument();
  });

  it('تؤكّد قبل تعطيل متغيّر، ولا تتيح تعطيل الافتراضي', async () => {
    client.getAdminProduct.mockResolvedValue(product({
      variants: [variant({ id: 70, isDefault: true, optionValueIds: [10] }), variant({ id: 71, optionValueIds: [11] })],
    }));
    client.setProductVariantStatus.mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('table');

    await user.click(within(row('S')).getByRole('button'));
    expect(screen.getByRole('menuitem', { name: 'admin.variants.deactivate' })).toBeDisabled();
    await user.keyboard('{Escape}');

    await user.click(within(row('M')).getByRole('button'));
    await user.click(screen.getByRole('menuitem', { name: 'admin.variants.deactivate' }));
    expect(client.setProductVariantStatus).not.toHaveBeenCalled();
    await user.click(await screen.findByRole('button', { name: 'admin.variants.confirmDeactivate.action' }));

    await waitFor(() => expect(client.setProductVariantStatus).toHaveBeenCalledWith(7, 71, false));
    expect(toast.success).toHaveBeenCalledWith('admin.variants.variantDeactivated');
  });
});

describe('الخيارات', () => {
  it('لا تتيح حذف قيمة يستخدمها متغيّر، وتتيح حذف غير المستخدمة ثم ترسل التعريف بمعرّفاته', async () => {
    client.setProductOptions.mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('table');

    const removeFor = (name) => screen.getByRole('button', { name: `admin.variants.removeValue:${JSON.stringify({ name })}` });
    expect(removeFor('S')).toBeDisabled();
    expect(removeFor('M')).toBeDisabled();
    await user.click(removeFor('L'));
    await user.click(screen.getByRole('button', { name: 'admin.variants.saveOptions' }));

    await waitFor(() => expect(client.setProductOptions).toHaveBeenCalledWith(7, { options: [{
      id: 1, names: { ar: 'المقاس', en: 'Size' },
      values: [{ id: 10, names: { ar: 'S', en: 'S' } }, { id: 11, names: { ar: 'M', en: 'M' } }],
    }] }));
    expect(toast.success).toHaveBeenCalledWith('admin.variants.optionsSaved');
  });

  it('تفحص الاسم بلغة المتجر قبل الإرسال، وتعرض رفض الخادم كما هو', async () => {
    client.setProductOptions.mockRejectedValue(new Error('بعد هذا التعديل يتطابق متغيّران'));
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('table');

    await user.click(screen.getByRole('button', { name: 'admin.variants.addOption' }));
    await user.click(screen.getByRole('button', { name: 'admin.variants.saveOptions' }));
    expect(await screen.findByText('admin.variants.errors.optionNameRequired:{"culture":"ar"}')).toBeInTheDocument();
    expect(client.setProductOptions).not.toHaveBeenCalled();

    const names = screen.getAllByLabelText('admin.variants.optionNameAr');
    await user.type(names[1], 'اللون');
    await user.type(screen.getByLabelText('admin.variants.valueNameAr 1', { selector: 'input[dir="rtl"]:not([value="S"])' }), 'أحمر');
    await user.click(screen.getByRole('button', { name: 'admin.variants.saveOptions' }));

    await waitFor(() => expect(client.setProductOptions).toHaveBeenCalledTimes(1));
    expect(client.setProductOptions.mock.calls[0][1].options[1]).toEqual({
      id: null, names: { ar: 'اللون' }, values: [{ id: null, names: { ar: 'أحمر' } }], existingVariantsValue: 0,
    });
    expect(await screen.findByText('بعد هذا التعديل يتطابق متغيّران')).toBeInTheDocument();
  });
});

describe('إنشاء التركيبات', () => {
  it('تعرض التركيبات الناقصة وحدها وتنشئ المختار منها بالتسعير المشترك', async () => {
    client.createProductVariants.mockResolvedValue({ ids: [72] });
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('table');

    const combinations = screen.getByRole('group', { name: 'admin.variants.combinationsLabel' });
    expect(within(combinations).getAllByRole('checkbox').map((box) => box.closest('label').textContent)).toEqual(['L']);
    await user.click(within(combinations).getByRole('checkbox'));
    await user.clear(screen.getByLabelText('admin.variants.priceLabel'));
    await user.type(screen.getByLabelText('admin.variants.priceLabel'), '24.5');
    await user.click(screen.getByRole('button', { name: 'admin.variants.create:{"count":1}' }));

    await waitFor(() => expect(client.createProductVariants).toHaveBeenCalledWith(7, { variants: [
      { optionValueIds: [12], price: 24.5, compareAtPrice: null, initialStock: 0, isActive: false },
    ] }));
    expect(toast.success).toHaveBeenCalledWith('admin.variants.created:{"count":1}');
  });

  it('تجتاز فحص الإتاحة البنيوي', async () => {
    const { container } = renderPage();
    await screen.findByRole('table');
    await expectNoViolations(container);
  });
});
