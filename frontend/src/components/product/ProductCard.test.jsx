// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

// ============================================================================
// نظام بطاقة المنتج: صيغ متعدّدة ومنطق واحد. ما يُختبر هو أن الصيغة تغيّر العرض لا السلوك —
// السعر والتوفّر والرابط والمفضّلة تتصرّف بالطريقة نفسها في كل صيغة.
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key }) }));
vi.mock('../../context/CartContext', () => ({ useCart: () => ({ add: vi.fn().mockResolvedValue(true) }) }));
vi.mock('../../context/WishlistContext', () => ({ useWishlist: () => ({ has: () => false, toggle: vi.fn() }) }));
vi.mock('../../app/TenantProvider', () => ({ useModule: () => true }));
vi.mock('./ProductImage', () => ({ default: () => <img alt="" /> }));
vi.mock('./ProductBadges', () => ({
  getProductName: (p) => p.name,
  PriceTag: ({ amount, compareAt }) => <span>{compareAt ? `${amount} was ${compareAt}` : String(amount)}</span>,
}));

const ProductCard = (await import('./ProductCard')).default;
const { CARD_VARIANTS } = await import('./ProductCard');

const product = (overrides = {}) => ({
  id: 7, slug: 'blue-shirt', name: 'Blue shirt', price: 20, currency: 'USD', stockQuantity: 5, ...overrides,
});

const renderCard = (props = {}) =>
  render(<MemoryRouter><ProductCard product={product()} {...props} /></MemoryRouter>);

beforeEach(() => vi.clearAllMocks());

describe('الصيغ', () => {
  it('كل صيغة معلنة تُرسم بلا سقوط', () => {
    for (const layout of CARD_VARIANTS) {
      const { unmount } = renderCard({ layout });
      expect(screen.getByRole('heading', { level: 3 })).toBeInTheDocument();
      unmount();
    }
  });

  it('صيغة غير معروفة تسقط إلى الشبكة لا إلى بطاقة بلا صنف', () => {
    const { container } = renderCard({ layout: 'hologram' });
    expect(container.querySelector('article').className).toContain('grid');
  });

  it('المضغوطة بلا زرّ إضافة — الزرّ يزاحم الاسم في صفّ ضيّق', () => {
    renderCard({ layout: 'compact' });
    expect(screen.queryByRole('button', { name: 'product.addToCart' })).toBeNull();
  });

  it('وكلّ صيغة أخرى تحمل زرّ الإضافة', () => {
    for (const layout of ['grid', 'list', 'featured']) {
      const { unmount } = renderCard({ layout });
      expect(screen.getByRole('button', { name: 'product.addToCart' })).toBeInTheDocument();
      unmount();
    }
  });
});

describe('السلوك واحد مهما اختلفت الصيغة', () => {
  it('الرابط إلى صفحة المنتج بالاسم في كل صيغة', () => {
    for (const layout of CARD_VARIANTS) {
      const { unmount } = renderCard({ layout });
      for (const link of screen.getAllByRole('link')) {
        expect(link).toHaveAttribute('href', '/products/blue-shirt');
      }
      unmount();
    }
  });

  it('نفاد المخزون يوقف الإضافة في كل صيغة تحمل زرّاً', () => {
    for (const layout of ['grid', 'list', 'featured']) {
      const { unmount } = render(
        <MemoryRouter><ProductCard product={product({ stockQuantity: 0 })} layout={layout} /></MemoryRouter>,
      );
      expect(screen.getByRole('button', { name: 'product.outOfStock' })).toBeDisabled();
      unmount();
    }
  });

  it('سعر المقارنة يظهر في كل صيغة حين يوجد', () => {
    for (const layout of CARD_VARIANTS) {
      const { unmount } = render(
        <MemoryRouter><ProductCard product={product({ compareAtPrice: 30 })} layout={layout} /></MemoryRouter>,
      );
      expect(screen.getByText('20 was 30')).toBeInTheDocument();
      unmount();
    }
  });
});
