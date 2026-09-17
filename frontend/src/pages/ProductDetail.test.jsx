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
  StockBadge: ({ quantity }) => <span data-testid="stock">{String(quantity)}</span>,
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
      <span data-testid="search">{useLocation().search}</span>
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
    expect(cart.add).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }), 2, undefined);
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

// ============================================================================
// اختيار المتغيّر (V3، P-08c): الاختيار صريح، والنافد يُعرض معطّلاً، والسعر والمتاح يتبعان المختار، والرابط يحمله —
// والمُرسَل إلى السلة هو معرّف المتغيّر لا تخميناً. المنطق نفسه مُختبَر في variantSelection.test.js؛ هذا وصله بالصفحة.
// ============================================================================
const withVariants = (overrides = {}) => product({
  price: 20, priceIsFrom: true, stockQuantity: 9,
  options: [
    { id: 1, position: 0, names: { en: 'Size' }, values: [{ id: 11, names: { en: 'S' } }, { id: 12, names: { en: 'M' } }] },
    { id: 2, position: 1, names: { en: 'Colour' }, values: [{ id: 21, names: { en: 'Red' } }, { id: 22, names: { en: 'Blue' } }] },
  ],
  variants: [
    { id: 71, optionValueIds: [11, 21], price: 20, compareAtPrice: null, available: 5 },
    { id: 72, optionValueIds: [11, 22], price: 20, compareAtPrice: null, available: 0 },
    { id: 73, optionValueIds: [12, 21], price: 25, compareAtPrice: null, available: 4 },
  ],
  ...overrides,
});

describe('ProductDetail variant selection', () => {
  const value = (name) => screen.getByRole('radio', { name: new RegExp(`^${name}`) });

  it('asks for an explicit choice: the button is disabled and the missing options are named', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants());
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(screen.getByText('product.addToCart').closest('button')).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('product.variant.chooseFirst');
    expect(screen.getByText('price 20')).toBeInTheDocument();
    expect(screen.queryByTestId('stock')).toBeNull();
    expect(cart.add).not.toHaveBeenCalled();
  });

  it('disables a sold-out value instead of choosing another one, and keeps the rest selectable', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants());
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    await userEvent.click(value('S'));

    expect(value('Blue')).toBeDisabled();
    expect(value('Blue')).toHaveAccessibleName(/product\.variant\.soldOut/);
    expect(value('Red')).toBeEnabled();
    expect(value('S')).toBeChecked();
    expect(value('M')).toBeEnabled();
  });

  it('follows the selection with the price, the availability and the link, then sends the variant id', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants());
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    await userEvent.click(value('M'));
    await userEvent.click(value('Red'));

    expect(screen.getByText('price 25')).toBeInTheDocument();
    expect(screen.getByTestId('stock').textContent).toBe('4');
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe('?variant=73'));

    await userEvent.click(screen.getByText('product.addToCart'));
    expect(cart.add).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }), 1, 73);
  });

  it('opens the variant its link names, and clears an id that no longer exists', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants());
    renderAt('/products/blue-shirt?variant=73');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(value('M')).toBeChecked();
    expect(screen.getByText('price 25')).toBeInTheDocument();
  });

  it('ignores a stale variant id and asks for a choice instead', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants());
    renderAt('/products/blue-shirt?variant=999');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(screen.getByText('product.addToCart').closest('button')).toBeDisabled();
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe(''));
  });

  it('blocks a sold-out selection, and never lets a combination without a variant be reached', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants());
    renderAt('/products/blue-shirt?variant=72');                 // S / Blue — موجود ونفد
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(screen.getByText('product.outOfStock').closest('button')).toBeDisabled();
    expect(screen.getByTestId('stock').textContent).toBe('0');
    expect(value('Blue')).toBeChecked();

    // M / Blue تركيبة لا متغيّر لها: تُعرض معطّلة، فالنقر لا يبدّل لون المتسوّق ولا يصل لتركيبة يرفضها الخادم.
    expect(value('M')).toBeDisabled();
    await userEvent.click(value('M'));
    expect(value('Blue')).toBeChecked();
    expect(value('S')).toBeChecked();
  });

  it('needs no choice when one variant remains, and adds it straight away', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants({
      variants: [{ id: 73, optionValueIds: [12, 21], price: 25, compareAtPrice: null, available: 4 }],
    }));
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(value('M')).toBeChecked();
    await userEvent.click(screen.getByText('product.addToCart'));
    expect(cart.add).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }), 1, 73);
  });

  it('shows a product whose every variant is sold out as unavailable', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants({
      variants: [
        { id: 71, optionValueIds: [11, 21], price: 20, compareAtPrice: null, available: 0 },
        { id: 73, optionValueIds: [12, 21], price: 25, compareAtPrice: null, available: 0 },
      ],
    }));
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(screen.getByText('product.outOfStock').closest('button')).toBeDisabled();
    expect(screen.getByTestId('stock').textContent).toBe('0');
    expect(value('S')).toBeDisabled();
  });

  it('clamps the quantity to the selected variant, so the server never gets more than it has', async () => {
    client.getProductBySlug.mockResolvedValue(withVariants({
      variants: [
        { id: 71, optionValueIds: [11, 21], price: 20, compareAtPrice: null, available: 5 },
        { id: 73, optionValueIds: [12, 21], price: 25, compareAtPrice: null, available: 1 },
      ],
    }));
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    await userEvent.click(value('S'));
    await userEvent.click(value('Red'));
    await userEvent.click(screen.getByLabelText('common.increaseQty'));
    await userEvent.click(screen.getByLabelText('common.increaseQty'));
    await userEvent.click(value('M'));
    await userEvent.click(screen.getByText('product.addToCart'));

    expect(cart.add).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }), 1, 73);
  });

  it('leaves a product without options exactly as it was', async () => {
    client.getProductBySlug.mockResolvedValue(product());
    renderAt('/products/blue-shirt');
    await screen.findByRole('heading', { level: 1, name: 'Blue shirt' });

    expect(screen.queryAllByRole('radio')).toHaveLength(0);
    expect(screen.queryByRole('status')).toBeNull();
    expect(screen.getByTestId('stock').textContent).toBe('3');
    expect(screen.getByText('product.addToCart').closest('button')).toBeEnabled();
  });
});
