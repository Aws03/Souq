// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

// ============================================================================
// صفحة السلة. أهمّ ما يُختبر هنا ليس التخطيط بل الحاجز أمام المال: سطر لم يعد متاحاً أو تجاوز
// المتاح يوقف زرّ الدفع — وهو الحاجز نفسه الذي في الدرج، لأن كليهما يقرأ الحالة ذاتها.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key) => key, i18n: { dir: () => 'ltr', language: 'en' } }),
}));
vi.mock('../app/usePageMetadata', () => ({ usePageMetadata: () => {} }));
vi.mock('../components/product/ProductBadges', () => ({
  formatPrice: (amount, currency) => `${amount} ${currency}`,
  getProductName: (item) => item.name,
}));
vi.mock('../components/product/ProductImage', () => ({ default: () => <img alt="" /> }));

const cart = vi.hoisted(() => ({ value: null }));
vi.mock('../context/CartContext', () => ({ useCart: () => cart.value }));

const Cart = (await import('./Cart')).default;

const line = (id, overrides = {}) => ({
  id, name: `product ${id}`, price: 10, qty: 1, lineTotal: 10, currency: 'USD',
  sellable: true, available: 5, ...overrides,
});

const basket = (items) => ({
  currency: 'USD', subtotal: items.length * 10, shipping: 0, total: items.length * 10,
  itemCount: items.length, shippingMethods: { required: false },
});

function setCart(items, { loaded = true } = {}) {
  cart.value = {
    basket: basket(items), items, loaded,
    inc: vi.fn(), dec: vi.fn(), remove: vi.fn(),
  };
}

function renderCart() {
  return render(
    <MemoryRouter initialEntries={['/cart']}>
      <Routes>
        <Route path="/cart" element={<Cart />} />
        <Route path="/checkout" element={<p>checkout page</p>} />
        <Route path="/" element={<p>storefront</p>} />
      </Routes>
    </MemoryRouter>
  );
}

beforeEach(() => setCart([line(1), line(2)]));

describe('Cart page', () => {
  it('يعرض سطراً لكل صنف', () => {
    renderCart();
    expect(screen.getByText('product 1')).toBeInTheDocument();
    expect(screen.getByText('product 2')).toBeInTheDocument();
  });

  it('لا يعرض سلّة فارغة قبل وصول ردّ الخادم', () => {
    // الحالة التي تجعل الزائر يظنّ سلّته ضاعت لحظةَ التحميل.
    setCart([], { loaded: false });
    renderCart();
    expect(screen.queryByText('cart.emptyTitle')).toBeNull();
  });

  it('سلّة فارغة ⇒ حالة فارغة لا جدول فارغ', () => {
    setCart([]);
    renderCart();
    expect(screen.getByText('cart.emptyTitle')).toBeInTheDocument();
    expect(screen.queryByText('cart.checkoutCta')).toBeNull();
  });

  it('يمضي إلى الدفع', async () => {
    renderCart();
    await userEvent.click(screen.getByText('cart.checkoutCta'));
    expect(screen.getByText('checkout page')).toBeInTheDocument();
  });

  it('سطر لم يعد متاحاً يوقف الدفع', async () => {
    setCart([line(1), line(2, { sellable: false })]);
    renderCart();

    expect(screen.getByText('cart.fixItems')).toBeInTheDocument();
    await userEvent.click(screen.getByText('cart.checkoutCta'));
    expect(screen.queryByText('checkout page')).toBeNull();
  });

  it('كمية تتجاوز المتاح توقف الدفع', async () => {
    setCart([line(1, { qty: 9, available: 2 })]);
    renderCart();

    await userEvent.click(screen.getByText('cart.checkoutCta'));
    expect(screen.queryByText('checkout page')).toBeNull();
  });

  it('لا يحسب مجاميع محلياً — يعرض ما أرسله الخادم', () => {
    cart.value = {
      ...cart.value,
      basket: { ...basket([line(1)]), subtotal: 10, shipping: 3, total: 999 },
      items: [line(1)],
    };
    renderCart();
    // 999 لا يساوي 10 + 3؛ الصفحة تعرض رقم الخادم لا مجموعها.
    expect(screen.getByText('999 USD')).toBeInTheDocument();
  });
});
