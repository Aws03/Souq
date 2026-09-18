// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// شاشة الطلبات — ولم يكن لها اختبار قبل M10.
//
// **هذا هو الاختبار الذي يُثبت العيب الذي سمّاه TD-25 مُغلقاً.** الدين يصف "ردّاً تجاوزه أحدثُ منه
// يصل فيُعرض"، والترقيم مكانٌ ضيّق لحدوثه؛ أمّا البحث فمكانه الطبيعي: كتابة "أحمد" كانت تُطلق طلباً
// لكل حرف، فإن وصل ردّ "أح" بعد ردّ "أحمد" كُتب فوقه — جدولُ نتائجِ بحثٍ لم يعد مكتوباً في الصندوق،
// على شاشةٍ يتّخذ التاجر قراراً منها (يفتح طلباً، يغيّر حالته).
//
// المفتاح الآن يحمل معايير العرض كلّها، فكلّ ردّ يُكتب في مفتاحه. والاختبار يُعلّق الردّ الأول بيده
// ويُطلقه **بعد** وصول الثاني — وهو ما كان يفشل قبل الهجرة.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    // `defaultValue` لا يُحقن في النصّ عند i18next (هو بديلٌ عند غياب المفتاح)، فلا يُحقن هنا أيضاً —
    // وإلا قرأ الاختبار مفتاحاً ملحقاً بوسيطه ولم يشبه ما يراه المستخدم.
    t: (key, values) => {
      const rest = Object.entries(values ?? {}).filter(([k]) => k !== 'defaultValue');
      return rest.length ? `${key}:${JSON.stringify(Object.fromEntries(rest))}` : key;
    },
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
vi.mock('../../i18n', () => ({ formatDate: (v) => `on ${v}` }));
vi.mock('../../components/product/ProductBadges', () => ({ formatPrice: (v, c) => `${c} ${v}` }));
vi.mock('./OrderDetailDrawer', () => ({
  default: ({ orderId, onClose }) => <div role="dialog">order {orderId}<button onClick={onClose}>close</button></div>,
}));

const client = vi.hoisted(() => ({ getOrders: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const Orders = (await import('./Orders')).default;

const order = (patch) => ({
  id: 1, orderNumber: 1001, customerId: 5, customerName: 'Sara', status: 'Paid',
  itemCount: 2, totalAmount: 30, currency: 'JOD', createdAt: '2026-09-18T00:00:00Z', ...patch,
});
const page = (items) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 2 });

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  client.getOrders.mockReset();
  client.getOrders.mockResolvedValue(page([order({})]));
});
afterEach(() => vi.useRealTimers());

// البحث مُهدَّأ 300ms — يُقدَّم الوقت صراحةً بدل انتظاره.
const settleDebounce = async () => { await vi.advanceTimersByTimeAsync(400); };

describe('القائمة', () => {
  it('الصفّ يُقرأ: رقم الطلب وحالته وإجماليه', async () => {
    render(withQueryClient(<Orders />));

    const row = (await screen.findByText('#1001')).closest('tr');
    expect(within(row).getByText('admin.orders.status.Paid')).toBeInTheDocument();
    expect(within(row).getByText('JOD 30')).toBeInTheDocument();
    expect(client.getOrders).toHaveBeenCalledWith({ status: '', search: '', page: 1, pageSize: 20 });
  });
});

describe('البحث', () => {
  it('ردّ بحثٍ تجاوزه المستخدم لا يُعرض فوق نتائج البحث الأحدث', async () => {
    // "sa" يُعلَّق بيدنا؛ "sara" يعود فوراً. ثم يصل ردّ "sa" **متأخّراً**.
    let releaseStale;
    client.getOrders.mockImplementation(({ search }) => {
      if (search === 'sa') {
        return new Promise((resolve) => {
          releaseStale = () => resolve(page([order({ id: 9, orderNumber: 1009, customerName: 'Salem' })]));
        });
      }
      if (search === 'sara') return Promise.resolve(page([order({ id: 2, orderNumber: 1002, customerName: 'Sara' })]));
      return Promise.resolve(page([order({})]));
    });

    render(withQueryClient(<Orders />));
    await screen.findByText('#1001');
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    const box = screen.getByPlaceholderText('admin.orders.searchPlaceholder');
    await user.type(box, 'sa');
    await settleDebounce();
    await user.type(box, 'ra');
    await settleDebounce();

    // نتائج "sara" معروضة.
    expect(await screen.findByText('#1002')).toBeInTheDocument();

    // الآن يصل ردّ "sa" المتأخّر. قبل الهجرة كان يُنادي setItems فيستبدل الجدول.
    releaseStale();
    await waitFor(() => expect(screen.getByText('#1002')).toBeInTheDocument());
    expect(screen.queryByText('#1009'), 'نتائج بحثٍ سابق ظهرت فوق الأحدث').toBeNull();
    // والصندوق ما زال يقول "sara": ما يُعرض وما هو مكتوب متّفقان.
    expect(box).toHaveValue('sara');
  });

  it('البحث مُهدَّأ: حرفٌ واحد لا يُطلق رحلة', async () => {
    render(withQueryClient(<Orders />));
    await screen.findByText('#1001');
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    client.getOrders.mockClear();

    await user.type(screen.getByPlaceholderText('admin.orders.searchPlaceholder'), 'sara');
    // قبل انقضاء المهلة: لا نداء بعد أربع ضغطات.
    expect(client.getOrders).not.toHaveBeenCalled();

    await settleDebounce();
    await waitFor(() => expect(client.getOrders).toHaveBeenCalledTimes(1));
    expect(client.getOrders).toHaveBeenCalledWith({ status: '', search: 'sara', page: 1, pageSize: 20 });
  });
});

describe('التصفية والترقيم', () => {
  it('تبديل الحالة يعيد إلى الصفحة الأولى', async () => {
    render(withQueryClient(<Orders />));
    await screen.findByText('#1001');
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await user.click(screen.getByRole('button', { name: /common\.next/ }));
    await waitFor(() => expect(client.getOrders).toHaveBeenCalledWith(
      expect.objectContaining({ page: 2 })));

    client.getOrders.mockClear();
    await user.selectOptions(screen.getByLabelText('admin.orders.colStatus'), 'Paid');

    // تصفيةٌ جديدة على الصفحة الثانية كانت ستُظهر "لا نتائج" لنتائج تبدأ من الصفحة الأولى.
    await waitFor(() => expect(client.getOrders).toHaveBeenCalledWith(
      expect.objectContaining({ status: 'Paid', page: 1 })));
  });
});
