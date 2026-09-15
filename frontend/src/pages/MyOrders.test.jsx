// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { withQueryClient } from '../test/queryWrapper';

// ============================================================================
// "طلباتي": الصفحة التي كان ملفّها فارغاً في المستودع (انظر app/moduleInvariants.test.js).
// ما يهمّ هنا ليس الشكل بل العقد: الصفحة من الرابط، كل سطر رابط إلى طلبه، ولا معرّف عميل
// يُرسَل إلى الخادم — الجلسة وحدها تحدّده.
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key, i18n: { dir: () => 'ltr' } }) }));
vi.mock('../app/usePageMetadata', () => ({ usePageMetadata: () => {} }));
vi.mock('../i18n', () => ({ formatDateTime: (value) => `at ${value}` }));
vi.mock('../components/product/ProductBadges', () => ({ formatPrice: (amount, currency) => `${amount} ${currency}` }));

const client = vi.hoisted(() => ({ getMyOrders: vi.fn() }));
vi.mock('../api/client', () => ({ api: client }));

const MyOrders = (await import('./MyOrders')).default;

const order = (id, overrides = {}) => ({
  id, orderNumber: 1000 + id, status: 'Shipped', totalAmount: 42.5, currency: 'JOD',
  createdAt: '2026-03-01T10:00:00Z', itemCount: 2, ...overrides,
});

const page = (items, overrides = {}) =>
  ({ items, pageNumber: 1, pageSize: 10, totalCount: items.length, totalPages: 1, ...overrides });

function renderAt(path = '/orders') {
  const Probe = () => <span data-testid="url">{useLocation().search}</span>;
  return render(withQueryClient(
    <MemoryRouter initialEntries={[path]}>
      <Probe />
      <Routes><Route path="/orders" element={<MyOrders />} /></Routes>
    </MemoryRouter>
  ));
}

beforeEach(() => { client.getMyOrders.mockReset(); });

describe('MyOrders', () => {
  it('يعرض سطراً لكل طلب، كلٌّ رابط إلى طلبه', async () => {
    client.getMyOrders.mockResolvedValue(page([order(7), order(9)]));
    renderAt();

    const links = await screen.findAllByRole('link');
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/orders/7', '/orders/9']);
    expect(screen.getAllByText('42.5 JOD')).toHaveLength(2);
  });

  it('لا يرسل معرّف عميل — الخادم يستخرجه من الجلسة', async () => {
    client.getMyOrders.mockResolvedValue(page([]));
    renderAt();

    await waitFor(() => expect(client.getMyOrders).toHaveBeenCalled());
    expect(client.getMyOrders).toHaveBeenCalledWith({ page: 1, pageSize: 10 });
  });

  it('يقرأ رقم الصفحة من الرابط لا من حالة داخلية', async () => {
    client.getMyOrders.mockResolvedValue(page([order(1)], { pageNumber: 3, totalPages: 4 }));
    renderAt('/orders?page=3');

    await waitFor(() => expect(client.getMyOrders).toHaveBeenCalledWith({ page: 3, pageSize: 10 }));
  });

  it('يتجاهل رقم صفحة غير صالح بدل إرساله للخادم', async () => {
    client.getMyOrders.mockResolvedValue(page([]));
    renderAt('/orders?page=-4');

    await waitFor(() => expect(client.getMyOrders).toHaveBeenCalledWith({ page: 1, pageSize: 10 }));
  });

  it('تغيير الصفحة يكتبها في الرابط', async () => {
    client.getMyOrders.mockResolvedValue(page([order(1)], { pageNumber: 1, totalPages: 3 }));
    renderAt();
    await screen.findAllByRole('link');

    await userEvent.click(screen.getByText('common.next'));

    await waitFor(() => expect(screen.getByTestId('url').textContent).toBe('?page=2'));
  });

  it('يعرض حالة فارغة لا قائمة فارغة', async () => {
    client.getMyOrders.mockResolvedValue(page([]));
    renderAt();

    expect(await screen.findByText('orders.emptyTitle')).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('يعرض خطأ الخادم قابلاً لإعادة المحاولة', async () => {
    client.getMyOrders.mockRejectedValue(new Error('boom'));
    renderAt();

    expect(await screen.findByRole('alert')).toHaveTextContent('boom');

    client.getMyOrders.mockResolvedValue(page([order(3)]));
    await userEvent.click(screen.getByText('common.retry'));

    expect(await screen.findByRole('link')).toHaveAttribute('href', '/orders/3');
  });
});
