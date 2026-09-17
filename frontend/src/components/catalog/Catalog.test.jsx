// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// استرجاع البحث في الواجهة (M3، ADR-0042). الدعوى التي تحرسها هذه الاختبارات: **الواجهة لا تخترع تصحيحاً
// ولا تُخفي أنّ تصحيحاً جرى.** الخادم يقرّر، والواجهة تعرض قراره ويبقى للمتسوّق أن يرفضه — وهو مبدأ "لا استبدال
// صامت" نفسه الذي يحرس اختيار المتغيّر في V3 (ADR-0041).
// ============================================================================
vi.mock('react-i18next', () => ({
  // المفتاح + متغيّراته: يكفي لإثبات ماذا عُرض وبأي قيم، بلا الاعتماد على نصّ ترجمة قد يتغيّر.
  useTranslation: () => ({
    t: (key, vars) => (vars ? `${key}(${Object.entries(vars).map(([k, v]) => `${k}=${v}`).join(',')})` : key),
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));
vi.mock('../product/ProductCard', () => ({ default: () => <div /> }));
vi.mock('../product/ProductBadges', () => ({
  formatPrice: (a) => String(a),
  getCategoryName: (c) => c.name,
}));

const client = vi.hoisted(() => ({ getProducts: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const Catalog = (await import('./Catalog')).default;

const emptyPage = (search = null) => ({ items: [], totalCount: 0, totalPages: 1, pageNumber: 1, search });
const onePage = (search = null) => ({
  items: [{ id: 1, slug: 'x', name: 'منتج', price: 1, currency: 'JOD' }],
  totalCount: 1, totalPages: 1, pageNumber: 1, search,
});

function Harness({ url }) {
  return withQueryClient(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/" element={<><Catalog categories={[]} /><Location /></>} />
      </Routes>
    </MemoryRouter>
  );
}

function Location() {
  const location = useLocation();
  // لا <output>: دوره الضمني status، وهو دور اللافتة التي تُختبَر هنا.
  return <div data-testid="url">{location.search}</div>;
}

beforeEach(() => {
  client.getProducts.mockReset();
});

describe('Catalog — استرجاع البحث', () => {
  it('يقول صراحةً أنّ البحث جرى بكلمة أخرى، ويسمّي الكلمتين', async () => {
    client.getProducts.mockResolvedValue(onePage({ term: 'مكلسة', searchedInstead: 'مكنسه', category: null }));

    render(<Harness url="/?q=مكلسة" />);

    const banner = await screen.findByRole('status');
    expect(banner).toHaveTextContent('searched=مكنسه');
    expect(banner).toHaveTextContent('original=مكلسة');
  });

  it('لا يعرض شيئاً حين لم يقل الخادم إنّه فعل شيئاً', async () => {
    client.getProducts.mockResolvedValue(onePage(null));

    render(<Harness url="/?q=مكنسة" />);

    await waitFor(() => expect(client.getProducts).toHaveBeenCalled());
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('الإصرار على الكلمة الأصلية يطلبها من الخادم بلا استرجاع', async () => {
    client.getProducts.mockResolvedValue(onePage({ term: 'مكلسة', searchedInstead: 'مكنسه', category: null }));

    render(<Harness url="/?q=مكلسة" />);
    await userEvent.click(await screen.findByRole('button', { name: /searchOriginalInstead/ }));

    // الرفض يظهر في الرابط (قابل للمشاركة والرجوع) ويُرسَل إلى الخادم — لا يُعالَج في الواجهة.
    await waitFor(() => expect(screen.getByTestId('url').textContent).toContain('exact=1'));
    await waitFor(() => expect(client.getProducts).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'مكلسة', exact: true })
    ));
  });

  it('بلا نتائج بحال يعرض الفئة المقترحة إجراءً لا نصّاً', async () => {
    client.getProducts.mockResolvedValue(emptyPage({
      term: 'زقفونيات', searchedInstead: null, category: { id: 9, slug: 'home', name: 'زقفونيات منزلية' },
    }));

    render(<Harness url="/?q=زقفونيات" />);

    expect(await screen.findByText(/noMatchTitle\(term=زقفونيات\)/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /browseCategory\(name=زقفونيات منزلية\)/ }));

    // تصفّح الفئة يترك كلمة البحث ويفتح الفئة: باب بدل نهاية مسدودة.
    await waitFor(() => {
      const url = screen.getByTestId('url').textContent;
      expect(url).toContain('cats=9');
      expect(url).not.toContain('q=');
    });
  });

  it('لا يقترح فئة حين لا يقترحها الخادم', async () => {
    client.getProducts.mockResolvedValue(emptyPage(null));

    render(<Harness url="/?q=لاشيء" />);

    expect(await screen.findByText('product.noMatchTitle')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /browseCategory/ })).not.toBeInTheDocument();
  });
});

describe('Catalog — ترتيب المطابقة', () => {
  it('مع كلمة بحث يكون الافتراضي الأكثر مطابقةً، وبلا كلمة الأحدث', async () => {
    client.getProducts.mockResolvedValue(onePage(null));

    render(<Harness url="/?q=مكنسة" />);
    await waitFor(() => expect(client.getProducts).toHaveBeenLastCalledWith(
      expect.objectContaining({ sortBy: 'Relevance' })
    ));

    client.getProducts.mockClear();
    render(<Harness url="/" />);
    await waitFor(() => expect(client.getProducts).toHaveBeenLastCalledWith(
      expect.objectContaining({ sortBy: 'Newest' })
    ));
  });

  it('يعرض زرّ الأكثر مطابقةً أثناء البحث وحده', async () => {
    client.getProducts.mockResolvedValue(onePage(null));

    const { unmount } = render(<Harness url="/?q=مكنسة" />);
    expect(await screen.findByRole('button', { name: 'store.filters.sort_relevance' })).toBeInTheDocument();
    unmount();

    render(<Harness url="/" />);
    await waitFor(() => expect(client.getProducts).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: 'store.filters.sort_relevance' })).not.toBeInTheDocument();
  });
});
