// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// شاشة جرد المخزون. الخادم يحرس الأرقام؛ ما يُختبر هنا أن **ما يُقرأ على الشاشة هو ما طُلب**:
//   • ردٌّ لصفحة تجاوزها التاجر لا يكتب فوق الصفحة المعروضة (الفجوة التي سمّاها TD-25، أُغلقت في M7)،
//   • عدد المنخفض لا يُسأل من جديد عند كل تصفّح (له مفتاحه، فهو لا يتعلّق بالصفحة)،
//   • والتصحيح يُنعش الجرد **وعدد المنخفض** معاً — لا الجدول وحده وشريط التنبيه يكذب بعده.
//
// لماذا تستحقّ هذه الشاشة اختباراً لا تملكه جاراتها؟ لأنها الشاشة التي يقرأ منها التاجر رقماً ثم يتصرّف
// بناءً عليه (يصحّح، يعيد الشراء). رقمٌ من صفحة أخرى هنا ليس خطأ عرض بل قرار مبنيّ على مخزون منتج آخر.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => ({ can: () => true }) }));
vi.mock('../../context/ToastContext', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('../../i18n', () => ({ formatDate: (v) => `on ${v}` }));

const client = vi.hoisted(() => ({
  getInventory: vi.fn(), getLowStock: vi.fn(),
  getVariantStockMovements: vi.fn(), adjustVariantStock: vi.fn(), setVariantStockThreshold: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const Inventory = (await import('./Inventory')).default;

const item = (patch) => ({
  productId: 1, variantId: 1, name: 'Item', sku: null, variantLabel: null, categoryName: 'Cat',
  onHand: 10, reserved: 0, available: 10, lowStockThreshold: 3, imageUrl: null, ...patch,
});
// ثلاث صفحات كي يبقى زرّ "التالي" مفعّلاً — السباق يحتاج انتقالين لا واحداً.
const pageOf = (items, page) => ({ items, page, pageSize: 50, totalCount: 120, totalPages: 3 });

const next = () => screen.getByRole('button', { name: /common\.next/ });
const previous = () => screen.getByRole('button', { name: /common\.previous/ });

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  client.getInventory.mockImplementation(({ page }) =>
    Promise.resolve(pageOf([item({ variantId: page, name: `Item on page ${page}` })], page)));
  client.getLowStock.mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 4, totalPages: 4 });
});

describe('القائمة', () => {
  it('صفّ لكل متغيّر بوصفه، والمتاح المنخفض يُميَّز، وعدد المنخفض من الخادم', async () => {
    client.getInventory.mockResolvedValue(pageOf([
      item({ variantId: 11, name: 'Shirt', variantLabel: 'XL', sku: 'SH-XL', available: 2, lowStockThreshold: 3 }),
      item({ variantId: 12, name: 'Shirt', variantLabel: 'L', available: 40 }),
    ], 1));
    render(withQueryClient(<Inventory />));

    const low = within(await screen.findByText('SH-XL').then((el) => el.closest('tr')));
    expect(low.getByText('XL')).toBeInTheDocument();
    // الحدّ 3 والمتاح 2 ⇒ منخفض. القيمة نفسها تُقرأ، فالتمييز اللوني وحده لا يكفي (ADR-0036).
    expect(low.getByText('2')).toBeInTheDocument();

    // العدد من totalCount لا من طول الصفحة — وهذا هو الفرق الذي يجعله صحيحاً مع الترقيم.
    expect(await screen.findByText('admin.inventory.lowStockAlert:{"count":4}')).toBeInTheDocument();
    expect(client.getInventory).toHaveBeenCalledWith({ page: 1, pageSize: 50 });
  });
});

describe('الترقيم', () => {
  // الصفحة 2 تُعلَّق بيدنا: هي النداء الذي يصل **بعد** أن انتقل التاجر عنها.
  const suspendPageTwo = () => {
    let release;
    client.getInventory.mockImplementation(({ page }) => (page === 2
      ? new Promise((resolve) => { release = () => resolve(pageOf([item({ variantId: 2, name: 'Item on page 2' })], 2)); })
      : Promise.resolve(pageOf([item({ variantId: page, name: `Item on page ${page}` })], page))));
    return () => release();
  };

  it('ردّ صفحة تجاوزها التاجر لا يكتب فوق الصفحة المعروضة', async () => {
    // **هذا هو الاختبار الذي يُغلق فجوة TD-25.** تحقّقتُ أنه يفشل على النمط القديم (useEffect + load):
    // الردّ المتأخّر كان يُنادي setItems فتظهر أسطر الصفحة 2 والترقيم يقول "صفحة 1".
    const releasePageTwo = suspendPageTwo();

    render(withQueryClient(<Inventory />));
    expect(await screen.findByText('Item on page 1')).toBeInTheDocument();
    const user = userEvent.setup();

    await user.click(next());                // الصفحة 2 معلّقة
    await user.click(previous());            // ثم يتراجع قبل أن تصل
    await waitFor(() => expect(screen.getByText('Item on page 1')).toBeInTheDocument());

    // الآن يصل ردّ الصفحة 2 المتأخّر: يُكتب في مفتاحه، لا على الشاشة.
    releasePageTwo();
    await waitFor(() => expect(screen.getByText('common.pageInfo:{"page":1,"totalPages":3}')).toBeInTheDocument());
    expect(screen.queryByText('Item on page 2')).toBeNull();
    expect(screen.getByText('Item on page 1')).toBeInTheDocument();
  });

  it('الصفحة المعروضة تبقى مقروءة أثناء جلب التالية', async () => {
    // keepPreviousData: لا جدول هياكل فارغ يقفز بين صفحتين. النمط القديم كان يرفع loading فيُخفي المعروض،
    // وهو ما يجعل التاجر يفقد سطره كلّما قلّب — فُحص بأنه يفشل على النمط القديم أيضاً.
    const releasePageTwo = suspendPageTwo();

    render(withQueryClient(<Inventory />));
    expect(await screen.findByText('Item on page 1')).toBeInTheDocument();
    const user = userEvent.setup();

    await user.click(next());
    expect(screen.getByText('Item on page 1')).toBeInTheDocument();

    releasePageTwo();
    expect(await screen.findByText('Item on page 2')).toBeInTheDocument();
    expect(screen.queryByText('Item on page 1')).toBeNull();
  });

  it('عدد المنخفض لا يُسأل من جديد عند كل تصفّح', async () => {
    render(withQueryClient(<Inventory />));
    await screen.findByText('Item on page 1');
    const user = userEvent.setup();

    await user.click(next());
    await screen.findByText('Item on page 2');
    await user.click(next());
    await screen.findByText('Item on page 3');

    // ثلاث صفحات، ونداء واحد للعدد: مفتاحه لا يحمل الصفحة، فالتصفّح لا يكلّف طلباً زائداً كل مرّة.
    expect(client.getInventory).toHaveBeenCalledTimes(3);
    expect(client.getLowStock).toHaveBeenCalledTimes(1);
  });
});

describe('التصحيح', () => {
  it('يُرسل الفارق وسببه ثم يُنعش الجرد وعدد المنخفض معاً', async () => {
    client.adjustVariantStock.mockResolvedValue(undefined);
    render(withQueryClient(<Inventory />));
    await screen.findByText('Item on page 1');
    const user = userEvent.setup();

    await user.click(within(screen.getByText('Item on page 1').closest('tr')).getByRole('button'));
    await user.click(await screen.findByRole('menuitem', { name: 'admin.inventory.adjust' }));

    const drawer = await screen.findByRole('dialog');
    await user.type(within(drawer).getByLabelText('admin.inventory.deltaLabel'), '-4');
    await user.type(within(drawer).getByLabelText('admin.inventory.reasonLabel'), 'تالف');
    await user.click(within(drawer).getByRole('button', { name: 'common.save' }));

    // فارق لا تعيين مطلق (Phase 0 C4): ما يُرسل هو −4، لا "6".
    await waitFor(() => expect(client.adjustVariantStock).toHaveBeenCalledWith(1, -4, 'تالف'));

    // وشريط التنبيه يُنعش مع الجدول: تصحيحٌ قد يُخرج سطراً من المنخفض أو يُدخله، فعددٌ قديم فوق جدول
    // جديد هو الحالة التي تجعل التاجر يثق برقم لم يعد صحيحاً.
    await waitFor(() => expect(client.getLowStock).toHaveBeenCalledTimes(2));
    expect(client.getInventory).toHaveBeenCalledTimes(2);
  });
});
