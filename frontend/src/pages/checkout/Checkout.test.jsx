// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// الدفع — الشاشة الوحيدة التي يقف عندها المال، وTD-32 يسمّيها آخر فجوة في تغطية المكوّنات.
//
// ما يُختبر هنا ليس التخطيط بل الحواجز: متى *لا* يُنشأ الطلب، وبأيّ كوبون يُنشأ، ومن أين
// يأتي المعروض من مبالغ. الخادم يُعيد التسعير عند الإنشاء في كل الأحوال (ADR-0028)، لكن ما
// يراه المشتري قبل الضغط يجب أن يطابق ما سيُدفع — وزرٌّ يُنشئ طلباً وسطرٌ في السلّة غير متاح
// يعني حجز مخزون لطلب سيفشل.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en', exists: () => false },
  }),
}));
vi.mock('../../app/TenantProvider', () => ({ useModule: () => true }));
vi.mock('../../components/product/ProductBadges', () => ({
  formatPrice: (a, c) => `${a} ${c}`,
  getProductName: (p) => p.name,
}));
vi.mock('../../components/product/ProductImage', () => ({ default: () => <img alt="" /> }));
vi.mock('./CardPaymentForm', () => ({ default: ({ order }) => <p>{`pay for ${order.orderId}`}</p> }));

const cart = vi.hoisted(() => ({ value: null }));
vi.mock('../../context/CartContext', () => ({ useCart: () => cart.value }));

const client = vi.hoisted(() => ({ getMyAddresses: vi.fn(), quoteBasket: vi.fn(), createOrder: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const Checkout = (await import('./Checkout')).default;

const item = (id, overrides = {}) => ({
  id, name: `product ${id}`, price: 10, qty: 1, lineTotal: 10, currency: 'USD',
  sellable: true, available: 5, ...overrides,
});

const quote = (overrides = {}) => ({
  currency: 'USD', subtotal: 10, discount: 0, shipping: 0, total: 10,
  coupon: null, shippingMethods: { required: false, options: [] }, ...overrides,
});

function setCart(items) {
  cart.value = {
    basket: { currency: 'USD', subtotal: items.length * 10, itemCount: items.length },
    items, total: items.length * 10, loaded: true, reload: vi.fn(),
  };
}

const Layout = () => <Outlet context={{ refreshProducts: vi.fn() }} />;

function renderCheckout() {
  return render(withQueryClient(
    <MemoryRouter initialEntries={['/checkout']}>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/checkout" element={<Checkout />} />
        </Route>
        <Route path="/confirmation" element={<p>confirmation</p>} />
      </Routes>
    </MemoryRouter>
  ));
}

const submit = () => userEvent.click(screen.getByRole('button', { name: 'checkout.continueToPayment' }));

beforeEach(() => {
  for (const fn of Object.values(client)) fn.mockReset();
  client.getMyAddresses.mockResolvedValue([{ id: 4, label: 'Home', recipientName: 'A', country: 'JO', city: 'X', line1: 'Y', isDefaultShipping: true }]);
  client.quoteBasket.mockResolvedValue(quote());
  setCart([item(1)]);
});

describe('Checkout barriers', () => {
  it('لا يُنشئ طلباً وسطرٌ في السلّة لم يعد متاحاً', async () => {
    setCart([item(1), item(2, { sellable: false })]);
    renderCheckout();
    await screen.findByText('checkout.shippingAddressTitle');

    expect(screen.getByRole('button', { name: 'checkout.continueToPayment' })).toBeDisabled();
    expect(client.createOrder).not.toHaveBeenCalled();
  });

  it('لا يُنشئ طلباً بلا عنوان', async () => {
    client.getMyAddresses.mockResolvedValue([]);   // لا دفتر عناوين ⇒ عنوان نصّي فارغ
    renderCheckout();
    await screen.findByText('checkout.shippingAddressTitle');

    await submit();

    expect(client.createOrder).not.toHaveBeenCalled();
    expect(screen.getByText('checkout.addressRequired')).toBeInTheDocument();
  });

  it('لا يُنشئ طلباً والمتجر يشترط طريقة شحن لم تُختر', async () => {
    client.quoteBasket.mockResolvedValue(quote({ shippingMethods: { required: true, options: [] } }));
    renderCheckout();
    await screen.findByText('checkout.shippingAddressTitle');

    expect(screen.getByRole('button', { name: 'checkout.continueToPayment' })).toBeDisabled();
    expect(client.createOrder).not.toHaveBeenCalled();
  });

  it('يُنشئ الطلب من السلّة لا من أسطر يرسلها المتصفّح', async () => {
    client.createOrder.mockResolvedValue({ orderId: 9, orderNumber: 1009, totalAmount: 10, currency: 'USD', clientSecret: 'cs' });
    renderCheckout();
    await screen.findByText('checkout.shippingAddressTitle');

    await submit();

    await waitFor(() => expect(client.createOrder).toHaveBeenCalledTimes(1));
    const payload = client.createOrder.mock.calls[0][0];
    expect(payload).not.toHaveProperty('items');
    expect(payload.shippingAddressId).toBe(4);
    expect(screen.getByText('pay for 9')).toBeInTheDocument();
  });
});

describe('Checkout money', () => {
  it('يعرض مجموع الخادم لا مجموعاً يحسبه بنفسه', async () => {
    // 77 لا يساوي 10؛ الشاشة تعرض رقم التسعير.
    client.quoteBasket.mockResolvedValue(quote({ total: 77, shipping: 5, discount: 3 }));
    renderCheckout();

    await waitFor(() => expect(screen.getByText('77 USD')).toBeInTheDocument());
  });

  it('يرسل الكوبون الذي قبله الخادم لا الذي كُتب', async () => {
    // المشتري يكتب SAVE10، والخادم يردّ بأنه طبّق SAVE10 بشكله المعياري.
    client.quoteBasket.mockResolvedValue(quote({ coupon: { code: 'SAVE10', applied: true }, discount: 3, total: 7 }));
    client.createOrder.mockResolvedValue({ orderId: 9, orderNumber: 1009, totalAmount: 7, currency: 'USD', clientSecret: 'cs' });
    renderCheckout();
    await screen.findByText('checkout.shippingAddressTitle');

    await userEvent.type(screen.getByPlaceholderText('checkout.couponPlaceholder'), 'save10');
    await userEvent.click(screen.getByRole('button', { name: 'checkout.applyCoupon' }));
    await waitFor(() => expect(screen.getByText(/checkout.couponApplied/)).toBeInTheDocument());

    await submit();

    await waitFor(() => expect(client.createOrder).toHaveBeenCalled());
    expect(client.createOrder.mock.calls[0][0].couponCode).toBe('SAVE10');
  });

  it('كوبون مرفوض لا يُرسَل مع الطلب', async () => {
    client.quoteBasket.mockResolvedValue(quote({ coupon: { code: 'BAD', applied: false, errorCode: 'InvalidCoupon', message: 'no' } }));
    client.createOrder.mockResolvedValue({ orderId: 9, orderNumber: 1009, totalAmount: 10, currency: 'USD', clientSecret: 'cs' });
    renderCheckout();
    await screen.findByText('checkout.shippingAddressTitle');

    await userEvent.type(screen.getByPlaceholderText('checkout.couponPlaceholder'), 'BAD');
    await userEvent.click(screen.getByRole('button', { name: 'checkout.applyCoupon' }));
    await waitFor(() => expect(screen.getByText('no')).toBeInTheDocument());

    await submit();

    await waitFor(() => expect(client.createOrder).toHaveBeenCalled());
    expect(client.createOrder.mock.calls[0][0].couponCode).toBeNull();
  });
});
