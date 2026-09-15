// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// نظرة العمل. جمهورها لا يعرف مفرداتنا، فما يُختبر هو أنها لا تكذب عليه:
//   • الإيراد لا يُسمّى ربحاً، والصفحة تقول صراحةً إنها لا تقيسه.
//   • كل حكم يحمل سببه.
//   • "لا نعرف بعد" لا تُعرض كـ"صفر بالمئة".
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
vi.mock('../../components/product/ProductBadges', () => ({ formatPrice: (v, c) => `${v} ${c}` }));
vi.mock('../../i18n', () => ({ formatDate: (v) => String(v).slice(0, 10) }));

const client = vi.hoisted(() => ({ getStoreDashboard: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const BusinessOverview = (await import('./BusinessOverview')).default;

const data = (overrides = {}) => ({
  currency: 'JOD',
  current: { revenue: 1200, refunds: 200, netRevenue: 1000, orders: 10, averageOrderValue: 120, newCustomers: 4 },
  previous: { revenue: 800, refunds: 0, netRevenue: 800, orders: 8, averageOrderValue: 100, newCustomers: 2 },
  trend: [{ bucket: '2026-03-01T00:00:00Z', revenue: 500, orders: 5 }],
  ordersByStatus: { Pending: 1, Paid: 4, Shipped: 2, Delivered: 3, Cancelled: 0 },
  topProducts: [{ productId: 1, name: 'Mug', unitsSold: 4, revenue: 400 }],
  topCategories: [], inventory: { healthy: 5, low: 0, outOfStock: 0 },
  pendingOrders: 1, pendingRefunds: 0, totalCustomers: 40, repeatCustomers: 10, ...overrides,
});

const renderPage = () => render(withQueryClient(<BusinessOverview />));

beforeEach(() => client.getStoreDashboard.mockReset());

describe('أمانة الصفحة', () => {
  it('تقول إن ما تعرضه إيراد لا ربح', async () => {
    client.getStoreDashboard.mockResolvedValue(data());
    renderPage();

    expect(await screen.findByText('admin.business.noProfit')).toBeInTheDocument();
    expect(screen.getByText('admin.business.noConversion')).toBeInTheDocument();
    expect(screen.getByText('admin.business.noForecast')).toBeInTheDocument();
  });

  it('لا تستعمل كلمة الربح في أي مؤشّر', async () => {
    client.getStoreDashboard.mockResolvedValue(data());
    const { container } = renderPage();
    await screen.findByText('admin.business.noProfit');

    expect(container.textContent).not.toMatch(/\bprofit\b|\bmargin\b/i);
  });

  it('تعرض صافي الإيراد لا الخام', async () => {
    client.getStoreDashboard.mockResolvedValue(data());
    renderPage();

    expect(await screen.findByText('1000 JOD')).toBeInTheDocument();
    expect(screen.queryByText('1200 JOD')).toBeNull();
  });
});

describe('الحكم', () => {
  it('يظهر مع سببه لا وحده', async () => {
    // بلا استرداد: نموّ نظيف ⇒ سليم. (الحالة الافتراضية أعلاه فيها ١٧٪ مُستردّ، وهي عمداً
    // "يحتاج انتباهاً" — النموّ مع استرداد ثلث الإيراد ليس صحّة.)
    client.getStoreDashboard.mockResolvedValue(data({
      current: { revenue: 1000, refunds: 0, netRevenue: 1000, orders: 10, averageOrderValue: 100, newCustomers: 4 },
      // ثلاثة منتجات متقاربة: أعلاها ٣٥٪ — دون عتبة التركّز. (منتج واحد يعني ١٠٠٪ من
      // المبيعات منه، ومنتجان بـ٤٠٠ و٣٦٠ يعنيان ٥٣٪ — وكلاهما خطر حقيقي لا ضجيج اختبار.)
      topProducts: [
        { productId: 1, name: 'Mug', unitsSold: 4, revenue: 400 },
        { productId: 2, name: 'Pot', unitsSold: 3, revenue: 380 },
        { productId: 3, name: 'Pan', unitsSold: 3, revenue: 360 },
      ],
    }));
    renderPage();

    expect(await screen.findByText('admin.business.health.healthy')).toBeInTheDocument();
    expect(screen.getByText(/admin\.business\.reason\.revenueUp/)).toBeInTheDocument();
  });

  it('استرداد سُدس الإيراد يمنع حكم "سليم" حتى مع نموّ', async () => {
    // الحالة الافتراضية: إيراد صاعد و١٧٪ مُستردّ.
    client.getStoreDashboard.mockResolvedValue(data());
    renderPage();

    expect(await screen.findByText('admin.business.health.attention')).toBeInTheDocument();
  });

  it('استرداد مرتفع يغلب النموّ', async () => {
    client.getStoreDashboard.mockResolvedValue(data({
      current: { revenue: 1000, refunds: 350, netRevenue: 650, orders: 10, averageOrderValue: 100, newCustomers: 4 },
    }));
    renderPage();

    expect(await screen.findByText('admin.business.health.attention')).toBeInTheDocument();
  });

  it('متجر بلا مبيعات ⇒ "البيانات لا تكفي" لا "سليم"', async () => {
    client.getStoreDashboard.mockResolvedValue(data({
      current: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      previous: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      ordersByStatus: {}, topProducts: [],
    }));
    renderPage();

    expect(await screen.findByText('admin.business.health.unknown')).toBeInTheDocument();
    expect(screen.queryByText('admin.business.health.healthy')).toBeNull();
  });
});

describe('الولاء', () => {
  it('يُعرض كنسبة حين يوجد عملاء', async () => {
    client.getStoreDashboard.mockResolvedValue(data());
    renderPage();

    expect(await screen.findByText(/admin\.business\.repeatRate.*25/)).toBeInTheDocument();
  });

  it('بلا عملاء ⇒ "غير قابل للقياس" لا صفر بالمئة', async () => {
    client.getStoreDashboard.mockResolvedValue(data({ totalCustomers: 0, repeatCustomers: 0 }));
    renderPage();

    expect(await screen.findByText('admin.business.repeatUnknown')).toBeInTheDocument();
  });
});

describe('الحالات الفارغة', () => {
  it('لا NaN ولا undefined لمتجر فارغ', async () => {
    client.getStoreDashboard.mockResolvedValue(data({
      current: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      previous: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      trend: [], ordersByStatus: {}, topProducts: [], totalCustomers: 0, repeatCustomers: 0,
      inventory: { healthy: 0, low: 0, outOfStock: 0 },
    }));
    const { container } = renderPage();

    await screen.findByText('admin.business.health.unknown');
    expect(container.textContent).not.toMatch(/NaN|undefined|Infinity/);
  });
});
