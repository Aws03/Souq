// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// نظرة المنصّة: مجاميع عبر المتاجر لا بيانات متجر بعينه.
// وحدود العدّ معروضة — الأرقام تشمل المؤرشف والملغى، وهذا نادراً ما يفترضه قارئ لوحة.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key, v) => (v?.name ? `${key}:${v.name}` : key), i18n: { language: 'en' } }),
}));
vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { fullName: 'Owner' }, logout: vi.fn(), can: () => true }),
}));

const client = vi.hoisted(() => ({ getPlatformStats: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const PlatformOverview = (await import('./PlatformOverview')).default;
const PlatformLayout = (await import('../../app/PlatformLayout')).default;

const stats = (overrides = {}) => ({
  tenantsByStatus: { Provisioning: 1, Active: 12, Suspended: 2, Archived: 3 },
  platformAccounts: 4, storeStaffAccounts: 37, customers: 940, products: 620,
  orders: 3100, ordersLast30Days: 210, ...overrides,
});

// الإطار والنظرة معاً كما يُرسمان فعلاً: زرّ الخروج في الإطار، والنظرة داخله.
const renderPlatform = () => render(withQueryClient(
  <MemoryRouter initialEntries={['/platform']}>
    <Routes>
      <Route path="/platform" element={<PlatformLayout />}>
        <Route index element={<PlatformOverview />} />
      </Route>
    </Routes>
  </MemoryRouter>,
));

beforeEach(() => client.getPlatformStats.mockReset());

describe('نظرة المنصّة', () => {
  it('تجمع المتاجر عبر كل الحالات', async () => {
    client.getPlatformStats.mockResolvedValue(stats());
    renderPlatform();

    // ١+١٢+٢+٣ = ١٨ متجراً؛ البطاقة تحمل التسمية نفسها فتُقرأ من بطاقتها لا من الصفحة كلّها.
    const storesCard = (await screen.findByText('platform.storesTotal')).closest('article');
    expect(storesCard).toHaveTextContent('18');
  });

  it('تعرض كل حالة ولو بصفر', async () => {
    client.getPlatformStats.mockResolvedValue(stats({ tenantsByStatus: { Active: 5 } }));
    renderPlatform();

    await screen.findByText('platform.status.Active');
    for (const status of ['Provisioning', 'Suspended', 'Archived']) {
      expect(screen.getByText(`platform.status.${status}`)).toBeInTheDocument();
    }
  });

  it('تقول كيف تُحسب الأرقام بدل تركها تُفهم خطأً', async () => {
    client.getPlatformStats.mockResolvedValue(stats());
    renderPlatform();

    expect(await screen.findByText('platform.countingNote')).toBeInTheDocument();
    expect(screen.getByText('platform.noTenantData')).toBeInTheDocument();
  });

  it('منصّة فارغة تعرض أصفاراً لا NaN', async () => {
    client.getPlatformStats.mockResolvedValue(stats({
      tenantsByStatus: {}, platformAccounts: 0, storeStaffAccounts: 0,
      customers: 0, products: 0, orders: 0, ordersLast30Days: 0,
    }));
    const { container } = renderPlatform();

    await screen.findByText('platform.countingNote');
    expect(container.textContent).not.toMatch(/NaN|undefined|Infinity/);
  });

  it('عطل الإحصاءات لا يمنع الخروج من الحساب', async () => {
    // الشاشة إطار المنصّة أيضاً: فشل رقم لا يجوز أن يحبس مالكها داخلها.
    client.getPlatformStats.mockRejectedValue(new Error('stats down'));
    renderPlatform();

    expect(await screen.findByRole('alert')).toHaveTextContent('stats down');
    // الإطار باقٍ: فشل رقم لا يحبس مالك المنصّة داخل شاشة بلا مخرج.
    expect(screen.getByRole('button', { name: 'platform.logout' })).toBeInTheDocument();

    // وإعادة المحاولة تنجح — فتُستهلك النتيجة المرفوضة ولا تبقى معلّقة عند التفكيك.
    client.getPlatformStats.mockResolvedValue(stats());
    await userEvent.click(screen.getByText('common.retry'));

    expect(await screen.findByText('platform.countingNote')).toBeInTheDocument();
  });
});
