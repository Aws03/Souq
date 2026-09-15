// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { withQueryClient } from '../test/queryWrapper';

// ============================================================================
// صفحة المنتج بعد أن صار رابطها بالاسم. الخطر الحقيقي في هذا التحويل ليس الشكل: كل ما يُطلب
// بعد تحميل المنتج (التقييمات، المشابهات، نموذج التقييم) كان يستعمل ما في الرابط كأنه معرّف —
// فرابط بالاسم يعني طلبات بمعرّف اسمه نصّ. هنا تُثبَّت القاعدة: الرابط للتوجيه، والمعرّف من المنتج.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key) => key, i18n: { dir: () => 'ltr', language: 'en' } }),
}));
vi.mock('../app/usePageMetadata', () => ({ usePageMetadata: () => {} }));
vi.mock('../components/product/ProductBadges', () => ({
  formatPrice: (a, c) => `${a} ${c}`,
  getProductName: (p) => p.name,
  getProductDescription: (p) => p.description,
  PriceTag: ({ amount }) => <span>price {amount}</span>,
  CategoryBadge: () => null,
  StockBadge: () => null,
}));
vi.mock('../components/product/ProductZoom', () => ({ default: () => <div /> }));
vi.mock('../components/store/ProductSection', () => ({ default: () => <div /> }));
vi.mock('../components/reviews/RatingSummary', () => ({ default: () => <div /> }));
vi.mock('../components/reviews/ReviewList', () => ({ default: () => <div /> }));
vi.mock('../components/reviews/ReviewForm', () => ({ default: ({ productId }) => <p>review form for {String(productId)}</p> }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true }) }));

const cart = vi.hoisted(() => ({ add: vi.fn() }));
vi.mock('../context/CartContext', () => ({ useCart: () => cart }));
vi.mock('../app/TenantProvider', () => ({ useModule: () => true }));

const client = vi.hoisted(() => ({
  getProduct: vi.fn(), getProductBySlug: vi.fn(), getRelatedProducts: vi.fn(), getProductReviews: vi.fn(),
}));
vi.mock('../api/client', () => ({ api: client }));

const ProductDetail = (await import('./ProductDetail')).default;

const product = (overrides = {}) => ({
  id: 7, slug: 'blue-shirt', name: 'Blue shirt', description: 'A shirt',
  price: 19.9, currency: 'USD', stockQuantity: 3, categoryId: 2, categoryName: 'Shirts',
  imageUrl: '/i.png', images: [], ...overrides,
});

const toast = vi.fn();

// الصفحة تعيش داخل تخطيط المتجر وتقرأ سياق الـ Outlet منه — الاختبار يعيد الشرط نفسه.
function Layout() {
  return (
    <>
      <span data-testid="url">{useLocation().pathname}</span>
      <Outlet context={{ showToast: toast }} />
    </>
  );
}

function renderAt(path) {
  return render(withQueryClient(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/products/:handle" element={<ProductDetail />} />
        </Route>
      </Routes>
    </MemoryRouter>
  ));
}

beforeEach(() => {
  for (const fn of Object.values(client)) fn.mockReset();
  cart.add.mockReset().mockResolvedValue(true);
  client.getRelatedProducts.mockResolvedValue([]);
  client.getProductReviews.mockResolvedValue({ items: [], totalCount: 0, pageSize: 5, averageRating: 0, distribution: {} });
  document.head.querySelector('#souq-structured-data')?.remove();
});

describe('ProductDetail routing', () => {
  it('رابط بالاسم يُحمّل بالاسم', async () => {
    client.getProductBySlug.mockResolvedValue(product());
    renderAt('/products/blue-shirt');

    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });
    expect(client.getProductBySlug).toHaveBeenCalledWith('blue-shirt');
    expect(client.getProduct).not.toHaveBeenCalled();
  });

  it('رابط قديم بالمعرّف يعمل ثم يُحوَّل إلى الاسم بلا شاشة فارغة', async () => {
    client.getProduct.mockResolvedValue(product());
    client.getProductBySlug.mockResolvedValue(product());
    renderAt('/products/7');

    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });
    expect(client.getProduct).toHaveBeenCalledWith('7');
    await waitFor(() => expect(screen.getByTestId('url').textContent).toBe('/products/blue-shirt'));

    // المهمّ أن المنتج يبقى معروضاً عبر التحويل: ما وصل بالمعرّف يُنسخ إلى مفتاح الاسم، فلا
    // يعود المستخدم إلى هيكل عظمي. التحقّق الخلفي بالاسم يجري بعدها ولا يُرى.
    expect(screen.getByRole('heading', { level: 1, name: 'Blue shirt' })).toBeInTheDocument();
    expect(client.getProductBySlug).toHaveBeenCalledTimes(1);
  });

  it('منتج بلا اسم لا يدخل حلقة تحويل', async () => {
    client.getProduct.mockResolvedValue(product({ slug: null }));
    renderAt('/products/7');

    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });
    expect(screen.getByTestId('url').textContent).toBe('/products/7');
  });

  it('ما بعد التحميل يُطلب بمعرّف المنتج لا بما في الرابط', async () => {
    client.getProductBySlug.mockResolvedValue(product());
    renderAt('/products/blue-shirt');

    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });
    await waitFor(() => expect(client.getRelatedProducts).toHaveBeenCalledWith(7));
    expect(client.getProductReviews).toHaveBeenCalledWith(7, { page: 1, pageSize: 5 });
    expect(screen.getByText('review form for 7')).toBeInTheDocument();
  });
});

describe('ProductDetail buying', () => {
  it('يضيف الكمية المختارة مرّة واحدة', async () => {
    client.getProductBySlug.mockResolvedValue(product());
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    await userEvent.click(screen.getByLabelText('common.increaseQty'));
    await userEvent.click(screen.getByText('product.addToCart'));

    expect(cart.add).toHaveBeenCalledTimes(1);
    expect(cart.add).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }), 2);
  });

  it('لا تتجاوز الكمية المتاح على الخادم', async () => {
    client.getProductBySlug.mockResolvedValue(product({ stockQuantity: 2 }));
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    const plus = screen.getByLabelText('common.increaseQty');
    await userEvent.click(plus);
    expect(plus).toBeDisabled();
  });

  it('نفاد المخزون: لا مُزيد كمية ولا إضافة', async () => {
    client.getProductBySlug.mockResolvedValue(product({ stockQuantity: 0 }));
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(screen.queryByLabelText('common.increaseQty')).toBeNull();
    expect(screen.getByRole('button', { name: 'product.outOfStock' })).toBeDisabled();
  });
});

describe('ProductDetail structured data', () => {
  const parsed = () => JSON.parse(document.head.querySelector('#souq-structured-data').textContent);

  it('ينشر سعر الخادم وتوفّره ومسار التصفّح', async () => {
    client.getProductBySlug.mockResolvedValue(product());
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    await waitFor(() => expect(document.head.querySelector('#souq-structured-data')).not.toBeNull());
    const [productBlock, breadcrumb] = parsed();
    expect(productBlock.offers).toMatchObject({ price: '19.9', priceCurrency: 'USD' });
    expect(productBlock.offers.availability).toBe('https://schema.org/InStock');
    expect(breadcrumb.itemListElement.map((i) => i.name)).toEqual(['nav.home', 'Shirts', 'Blue shirt']);
  });

  it('لا تقييم مجمّع لمنتج بلا تقييمات', async () => {
    client.getProductBySlug.mockResolvedValue(product());
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    await waitFor(() => expect(document.head.querySelector('#souq-structured-data')).not.toBeNull());
    expect(parsed()[0]).not.toHaveProperty('aggregateRating');
  });
});
