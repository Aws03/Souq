// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { expectNoViolations } from './test/axe';
import { withQueryClient } from './test/queryWrapper';

// ============================================================================
// إتاحة أسطح المتجر التي يمرّ بها كل زبون. الفحص بنيويّ (axe في jsdom) — يمسك الصورة بلا
// بديل، والزرّ بلا اسم، والحقل بلا تسمية، والعنوان الذي يقفز مستوى. لا يمسك التباين ولا
// ترتيب التركيز؛ تلك للمتصفّح.
// ============================================================================
vi.mock('react-i18next', () => ({
  initReactI18next: { type: '3rdParty', init: () => {} },
  useTranslation: () => ({ t: (key) => key, i18n: { dir: () => 'ltr', language: 'en' } }),
  withTranslation: () => (Component) => {
    const Translated = (props) => <Component {...props} t={(key) => key} />;
    Translated.displayName = 'Translated';
    return Translated;
  },
}));
vi.mock('./app/usePageMetadata', () => ({ usePageMetadata: () => {} }));
vi.mock('./i18n', () => ({ default: {}, formatDate: (v) => String(v), formatDateTime: (v) => String(v) }));
vi.mock('./app/TenantProvider', () => ({
  useModule: () => true,
  useStoreConfig: () => ({ settings: { seo: { description: { en: 'A shop' } }, locale: { defaultCulture: 'en' } } }),
}));
vi.mock('./app/StoreBrand', () => ({
  default: () => <span>Store</span>,
  useStoreName: () => 'Test Store',
}));
vi.mock('./components/product/ProductBadges', () => ({
  formatPrice: (a, c) => `${a} ${c}`,
  getProductName: (p) => p.name,
  getProductDescription: (p) => p.description,
  PriceTag: ({ amount }) => <span>{`price ${amount}`}</span>,
  CategoryBadge: () => null,
  StockBadge: () => null,
}));
vi.mock('./components/product/ProductImage', () => ({ default: () => <img alt="" /> }));
vi.mock('./context/ToastContext', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('./context/WishlistContext', () => ({ useWishlist: () => ({ items: [], has: () => false, toggle: vi.fn() }) }));

const cart = vi.hoisted(() => ({ value: null }));
vi.mock('./context/CartContext', () => ({ useCart: () => cart.value }));

const client = vi.hoisted(() => ({ getMyOrders: vi.fn(), getProducts: vi.fn() }));
vi.mock('./api/client', () => ({ api: client }));

const Cart = (await import('./pages/Cart')).default;
const NotFound = (await import('./pages/NotFound')).default;
const Hero = (await import('./components/store/Hero')).default;
const Drawer = (await import('./components/common/Drawer')).default;
const AccountLayout = (await import('./pages/account/AccountLayout')).default;
const MyOrders = (await import('./pages/MyOrders')).default;
const ErrorBoundary = (await import('./components/common/ErrorBoundary')).default;

const line = (id) => ({
  id, name: `product ${id}`, price: 10, qty: 1, lineTotal: 10, currency: 'USD', sellable: true, available: 5,
});

function renderPage(element, path = '/') {
  return render(withQueryClient(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={<Outlet context={{ showToast: vi.fn(), refreshKey: 0, categories: [] }} />}>
          <Route path={path} element={element} />
        </Route>
      </Routes>
    </MemoryRouter>
  ));
}

const clean = async (container) => expect(await expectNoViolations(container)).toEqual([]);

describe('storefront accessibility', () => {
  it('صفحة السلة بمحتواها', async () => {
    cart.value = {
      basket: { currency: 'USD', subtotal: 20, shipping: 0, total: 20, itemCount: 2, shippingMethods: { required: false } },
      items: [line(1), line(2)], loaded: true, inc: vi.fn(), dec: vi.fn(), remove: vi.fn(),
    };
    const { container } = renderPage(<Cart />, '/cart');
    await clean(container);
  });

  it('صفحة السلة فارغة', async () => {
    cart.value = {
      basket: { currency: 'USD', subtotal: 0, shipping: 0, total: 0, itemCount: 0, shippingMethods: { required: false } },
      items: [], loaded: true, inc: vi.fn(), dec: vi.fn(), remove: vi.fn(),
    };
    const { container } = renderPage(<Cart />, '/cart');
    await clean(container);
  });

  it('صفحة 404', async () => {
    const { container } = renderPage(<NotFound />);
    await clean(container);
  });

  it('بانر المتجر', async () => {
    const { container } = renderPage(<Hero />);
    await clean(container);
  });

  it('درج مفتوح (نافذة حوارية)', async () => {
    const { container } = render(
      <Drawer open onClose={vi.fn()} title="Cart"><p>body</p></Drawer>
    );
    await clean(container);
  });

  it('قشرة الحساب بتنقّلها', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/account']}>
        <Routes>
          <Route element={<AccountLayout />}>
            <Route path="/account" element={<p>panel</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    );
    await clean(container);
  });

  it('قائمة الطلبات', async () => {
    client.getMyOrders.mockResolvedValue({
      items: [{ id: 3, orderNumber: 1003, status: 'Shipped', totalAmount: 10, currency: 'USD', createdAt: '2026-01-01T00:00:00Z' }],
      pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1,
    });
    const { container, findByRole } = renderPage(<MyOrders />, '/orders');
    await findByRole('link');
    await clean(container);
  });

  it('شاشة الخطأ العامّة', async () => {
    const Boom = () => { throw new Error('x'); };
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const { container } = render(<MemoryRouter><ErrorBoundary><Boom /></ErrorBoundary></MemoryRouter>);
    await clean(container);
    vi.restoreAllMocks();
  });
});
