// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// لوحة المدير. ما يُختبر ليس التخطيط بل أمانة العرض:
//   • صافي الإيراد لا الإيراد الخام في البطاقة الأولى (الفرق هو ما استُردّ فعلاً).
//   • لا ربح ولا معدّل تحويل — وغيابهما مُعلَن على الشاشة لا مسكوت عنه.
//   • متجر جديد يرى إرشاداً لا مخطّطات أصفار.
//   • تبديل المدّة يطلب المدّة الجديدة بمفتاحها المغلق.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && values.count !== undefined ? `${key}:${values.count}` : key),
    i18n: { dir: () => 'ltr', language: 'en' },
  }),
}));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => ({ user: { fullName: 'Manager' } }) }));
vi.mock('../../components/product/ProductBadges', () => ({ formatPrice: (v, c) => `${v} ${c}` }));
vi.mock('../../i18n', () => ({ formatDate: (v) => String(v).slice(0, 10) }));

const client = vi.hoisted(() => ({ getStoreDashboard: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const Dashboard = (await import('./Dashboard')).default;

const busy = (overrides = {}) => ({
  range: 'Last30Days', from: '2026-02-14T00:00:00Z', to: '2026-03-16T00:00:00Z', currency: 'JOD',
  current: { revenue: 1200, refunds: 200, netRevenue: 1000, orders: 10, averageOrderValue: 120, newCustomers: 4 },
  previous: { revenue: 800, refunds: 0, netRevenue: 800, orders: 8, averageOrderValue: 100, newCustomers: 2 },
  trend: [{ bucket: '2026-03-01T00:00:00Z', revenue: 500, orders: 5 }],
  ordersByStatus: { Pending: 2, Paid: 3, Shipped: 1, Delivered: 4, Cancelled: 1 },
  topProducts: [{ productId: 1, name: 'Thermal Mug', unitsSold: 12, revenue: 480 }],
  topCategories: [{ categoryId: 2, name: 'kitchen', unitsSold: 12, revenue: 480 }],
  inventory: { healthy: 10, low: 2, outOfStock: 1 },
  pendingOrders: 2, pendingRefunds: 1, totalCustomers: 20, repeatCustomers: 5, ...overrides,
});

const brandNew = () => busy({
  current: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
  previous: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
  trend: [], ordersByStatus: {}, topProducts: [], topCategories: [],
  inventory: { healthy: 0, low: 0, outOfStock: 0 },
  pendingOrders: 0, pendingRefunds: 0, totalCustomers: 0, repeatCustomers: 0,
});

const renderDashboard = () => render(withQueryClient(<MemoryRouter><Dashboard /></MemoryRouter>));

beforeEach(() => client.getStoreDashboard.mockReset());

describe('أمانة الأرقام', () => {
  it('البطاقة الأولى صافي الإيراد لا الإيراد الخام', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    renderDashboard();

    // 1000 هو الصافي؛ 1200 هو الخام قبل ما استُردّ. عرض الخام يُبالغ بما رُدّ.
    expect(await screen.findByText('1000 JOD')).toBeInTheDocument();
    expect(screen.queryByText('1200 JOD')).toBeNull();
  });

  it('يقول صراحةً إنه لا يقيس الربح ولا معدّل التحويل', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    renderDashboard();

    await screen.findByText('1000 JOD');
    expect(screen.getByText('admin.reports.noProfit')).toBeInTheDocument();
    expect(screen.getByText('admin.reports.noConversion')).toBeInTheDocument();
  });

  it('كل بطاقة تحمل صيغتها كي تُقرأ كما تعني', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    renderDashboard();

    await screen.findByText('1000 JOD');
    const card = screen.getByText('admin.reports.kpi.netRevenue').closest('article');
    expect(card).toHaveAttribute('title', 'admin.reports.formula.netRevenue');
  });
});

describe('التنبيهات', () => {
  it('تُعرض أولاً وتقود إلى الشاشة التي تحلّها', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    renderDashboard();

    const alert = await screen.findByText('admin.reports.alert.outOfStock:1');
    expect(alert.closest('a')).toHaveAttribute('href', '/admin/inventory');
  });

  it('متجر سليم يُقال له ذلك بدل ترك الفراغ', async () => {
    client.getStoreDashboard.mockResolvedValue(busy({
      inventory: { healthy: 9, low: 0, outOfStock: 0 }, pendingOrders: 0, pendingRefunds: 0,
    }));
    renderDashboard();

    expect(await screen.findByText('admin.reports.allClear')).toBeInTheDocument();
  });
});

describe('الحالات الفارغة', () => {
  it('متجر جديد يرى إرشاد بدء لا مخطّطات أصفار', async () => {
    client.getStoreDashboard.mockResolvedValue(brandNew());
    renderDashboard();

    expect(await screen.findByText('admin.reports.emptyTitle')).toBeInTheDocument();
    expect(screen.queryByText('admin.reports.trendTitle')).toBeNull();
  });

  it('لا NaN ولا undefined في أي حالة فارغة', async () => {
    client.getStoreDashboard.mockResolvedValue(brandNew());
    const { container } = renderDashboard();

    await screen.findByText('admin.reports.emptyTitle');
    expect(container.textContent).not.toMatch(/NaN|undefined|Infinity/);
  });
});

describe('المدّة', () => {
  it('تبديلها يطلب المدّة الجديدة بمفتاحها', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    renderDashboard();
    await screen.findByText('1000 JOD');

    await userEvent.click(screen.getByRole('button', { name: 'admin.reports.range.Last7Days' }));

    await waitFor(() => expect(client.getStoreDashboard).toHaveBeenCalledWith('Last7Days'));
  });

  it('المدّة المختارة مُعلَنة لقارئ الشاشة', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    renderDashboard();
    await screen.findByText('1000 JOD');

    expect(screen.getByRole('button', { name: 'admin.reports.range.Last30Days' }))
      .toHaveAttribute('aria-pressed', 'true');
  });
});

describe('المخطّطات', () => {
  it('لكل مخطّط بديل نصّي يحمل الأرقام نفسها', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    renderDashboard();
    await screen.findByText('1000 JOD');

    // SVG لا يقرؤه قارئ شاشة: الجدول المخفي هو نسخته المقروءة.
    const tables = screen.getAllByRole('table', { hidden: false });
    expect(tables.length).toBeGreaterThanOrEqual(3);
    expect(screen.getByRole('row', { name: /Thermal Mug/ })).toBeInTheDocument();
  });

  it('الرسم نفسه مخفيّ عن شجرة الإتاحة', async () => {
    client.getStoreDashboard.mockResolvedValue(busy());
    const { container } = renderDashboard();
    await screen.findByText('1000 JOD');

    for (const svg of container.querySelectorAll('svg')) {
      expect(svg.closest('[aria-hidden="true"]')).not.toBeNull();
    }
  });
});
