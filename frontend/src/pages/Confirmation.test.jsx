// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

// ============================================================================
// شاشة التأكيد. حالتان كانتا خاطئتين وتُختبران هنا إلى الأبد:
//   • وعد تسليم "3–5 أيام عمل" مكتوب في الواجهة لا يعرفه أحد — المدّة الآن من الطلب، وبلا
//     مدّة لا وعد.
//   • تحديث الصفحة بعد الدفع كان يعيد المشتري للرئيسية — الرقم في الرابط الآن.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
vi.mock('../app/usePageMetadata', () => ({ usePageMetadata: () => {} }));
vi.mock('../components/product/ProductBadges', () => ({ formatPrice: (a, c) => `${a} ${c}` }));

const client = vi.hoisted(() => ({ getOrder: vi.fn() }));
vi.mock('../api/client', () => ({ api: client }));

const Confirmation = (await import('./Confirmation')).default;

const serverOrder = (overrides = {}) => ({
  id: 12, orderNumber: 1042, totalAmount: 55, currency: 'USD',
  shippingMethod: 'Express', shippingMinDays: 2, shippingMaxDays: 4, ...overrides,
});

function renderAt(entry) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/confirmation" element={<Confirmation />} />
        <Route path="/" element={<p>storefront</p>} />
        <Route path="/orders" element={<p>my orders</p>} />
      </Routes>
    </MemoryRouter>
  );
}

const placed = { order: { orderId: 12, orderNumber: 1042, total: 55, currency: 'USD' } };

beforeEach(() => { client.getOrder.mockReset(); });

describe('Confirmation', () => {
  it('ينجو من تحديث الصفحة: الرقم في الرابط يُقرأ من الخادم', async () => {
    client.getOrder.mockResolvedValue(serverOrder());
    renderAt('/confirmation?order=12');

    expect(await screen.findByText('#1042')).toBeInTheDocument();
    expect(client.getOrder).toHaveBeenCalledWith(12);
    expect(screen.queryByText('storefront')).toBeNull();
  });

  it('مدّة التسليم من الطلب لا من الواجهة', async () => {
    client.getOrder.mockResolvedValue(serverOrder({ shippingMinDays: 2, shippingMaxDays: 4 }));
    renderAt('/confirmation?order=12');

    expect(await screen.findByText(/checkout\.shipping\.daysRange/)).toHaveTextContent('"min":2');
  });

  it('متجر بلا مدّة محدّدة لا يَعِد بشيء', async () => {
    client.getOrder.mockResolvedValue(serverOrder({ shippingMinDays: null, shippingMaxDays: null, shippingMethod: null }));
    renderAt('/confirmation?order=12');

    expect(await screen.findByText('#1042')).toBeInTheDocument();
    expect(screen.queryByText(/daysRange|daysExact/)).toBeNull();
  });

  it('طلب ليس له ⇒ قائمة طلباته لا تأكيد فارغ', async () => {
    client.getOrder.mockRejectedValue(Object.assign(new Error('not found'), { status: 404 }));
    renderAt('/confirmation?order=999');

    expect(await screen.findByText('my orders')).toBeInTheDocument();
  });

  it('بلا رقم طلب إطلاقاً ⇒ المتجر', () => {
    renderAt('/confirmation');
    expect(screen.getByText('storefront')).toBeInTheDocument();
    expect(client.getOrder).not.toHaveBeenCalled();
  });

  it('يرسم فوراً من حالة التوجيه ولا ينتظر الخادم', () => {
    client.getOrder.mockReturnValue(new Promise(() => {})); // لا يردّ أبداً
    render(
      <MemoryRouter initialEntries={[{ pathname: '/confirmation', search: '?order=12', state: placed }]}>
        <Routes><Route path="/confirmation" element={<Confirmation />} /></Routes>
      </MemoryRouter>
    );

    expect(screen.getByText('#1042')).toBeInTheDocument();
    expect(screen.getByText('55 USD')).toBeInTheDocument();
  });

  it('عطل شبكة بعد دفع ناجح لا يمحو التأكيد', async () => {
    client.getOrder.mockRejectedValue(new Error('offline'));
    render(
      <MemoryRouter initialEntries={[{ pathname: '/confirmation', search: '?order=12', state: placed }]}>
        <Routes>
          <Route path="/confirmation" element={<Confirmation />} />
          <Route path="/orders" element={<p>my orders</p>} />
        </Routes>
      </MemoryRouter>
    );

    expect(await screen.findByText('#1042')).toBeInTheDocument();
    expect(screen.queryByText('my orders')).toBeNull();
  });
});
