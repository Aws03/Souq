// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { withQueryClient } from '../test/queryWrapper';

// ============================================================================
// صفحة التتبّع العامّة — الصفحة الوحيدة بلا مصادقة، وملفّها أيضاً كان فارغاً في المستودع.
// أهمّ حالتين: رمز خاطئ يعطي رسالة مترجمة لا نصّ خادم خام (الزائر هنا قد لا يكون عميل
// المتجر)، وما تعرضه الصفحة لا يتجاوز ما يكشفه عقد الخادم.
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key, i18n: { dir: () => 'rtl' } }) }));
vi.mock('../app/usePageMetadata', () => ({ usePageMetadata: () => {} }));
vi.mock('../i18n', () => ({ formatDateTime: (value) => `at ${value}` }));
vi.mock('../context/ToastContext', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

const client = vi.hoisted(() => ({ trackOrder: vi.fn() }));
vi.mock('../api/client', () => ({ api: client }));

const OrderTracking = (await import('./OrderTracking')).default;

const tracking = (overrides = {}) => ({
  orderNumber: 1042, status: 'Shipped', trackingNumber: 'TRK-99', shippingCarrier: 'Aramex',
  createdAt: '2026-03-01T10:00:00Z', trackingUrl: 'https://carrier.example/TRK-99',
  history: [
    { status: 'Pending', changedAt: '2026-03-01T10:00:00Z' },
    { status: 'Paid', changedAt: '2026-03-01T11:00:00Z' },
    { status: 'Shipped', changedAt: '2026-03-02T09:00:00Z' },
  ],
  ...overrides,
});

const renderToken = (token) => render(withQueryClient(
  <MemoryRouter initialEntries={[`/track/${token}`]}>
    <Routes><Route path="/track/:token" element={<OrderTracking />} /></Routes>
  </MemoryRouter>
));

beforeEach(() => { client.trackOrder.mockReset(); });

describe('OrderTracking', () => {
  it('يتتبّع بالرمز الذي في الرابط', async () => {
    client.trackOrder.mockResolvedValue(tracking());
    renderToken('a1b2c3');

    expect(await screen.findByText('TRK-99')).toBeInTheDocument();
    expect(client.trackOrder).toHaveBeenCalledWith('a1b2c3');
  });

  it('يعرض الخط الزمني كاملاً وآخر خطوة هي الحالية', async () => {
    client.trackOrder.mockResolvedValue(tracking());
    renderToken('a1b2c3');

    const steps = await screen.findAllByRole('listitem');
    expect(steps).toHaveLength(3);
    expect(steps[2]).toHaveTextContent('orders.status.Shipped');
  });

  it('رابط الناقل الخارجي يحمل rel=noopener', async () => {
    client.trackOrder.mockResolvedValue(tracking());
    renderToken('a1b2c3');

    const carrierLink = await screen.findByText('orders.trackShipment');
    expect(carrierLink).toHaveAttribute('href', 'https://carrier.example/TRK-99');
    expect(carrierLink.getAttribute('rel')).toContain('noopener');
  });

  it('رمز غير موجود ⇒ رسالة مترجمة لا نصّ الخادم', async () => {
    const notFound = Object.assign(new Error('الطلب غير موجود'), { status: 404 });
    client.trackOrder.mockRejectedValue(notFound);
    const { container } = renderToken('deadbeef');

    expect(await screen.findByText('orders.trackNotFoundTitle')).toBeInTheDocument();
    expect(container.textContent).not.toContain('الطلب غير موجود');
  });

  it('عطل غير 404 يظهر كخطأ لا كرابط ميّت', async () => {
    client.trackOrder.mockRejectedValue(Object.assign(new Error('gateway down'), { status: 503 }));
    renderToken('a1b2c3');

    expect(await screen.findByRole('alert')).toHaveTextContent('gateway down');
    expect(screen.queryByText('orders.trackNotFoundTitle')).toBeNull();
  });

  it('طلب بلا شحنة بعد: لا صندوق رقم تتبّع فارغ', async () => {
    client.trackOrder.mockResolvedValue(tracking({ trackingNumber: null, shippingCarrier: null, trackingUrl: null, status: 'Paid' }));
    renderToken('a1b2c3');

    expect(await screen.findAllByText('orders.status.Paid')).not.toHaveLength(0);
    expect(screen.queryByText('orders.trackingNumber')).toBeNull();
  });
});
