// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

// ============================================================================
// قشرة الحساب: ما يهمّ هو التنقّل — أن يصل العميل من ملفه إلى عناوينه إلى طلباته، وأن
// يظهر رابط المفضّلة بشرط الوحدة نفسه الذي يستعمله شريط التنقّل (وحدة معطّلة ⇒ رابط ميّت).
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key }) }));

const tenant = vi.hoisted(() => ({ modules: [] }));
vi.mock('../../app/TenantProvider', () => ({ useModule: (m) => tenant.modules.includes(m) }));

const AccountLayout = (await import('./AccountLayout')).default;

const renderAt = (path) => render(
  <MemoryRouter initialEntries={[path]}>
    <Routes>
      <Route element={<AccountLayout />}>
        <Route path="/account" element={<p>profile panel</p>} />
        <Route path="/account/addresses" element={<p>addresses panel</p>} />
        <Route path="/orders" element={<p>orders panel</p>} />
      </Route>
    </Routes>
  </MemoryRouter>
);

const navLinks = () =>
  [...screen.getByRole('navigation').querySelectorAll('a')].map((a) => a.getAttribute('href'));

beforeEach(() => { tenant.modules = []; });

describe('AccountLayout', () => {
  it('يعرض الصفحة المطلوبة داخل القشرة', () => {
    renderAt('/account/addresses');
    expect(screen.getByText('addresses panel')).toBeInTheDocument();
  });

  it('يجمع أقسام الحساب في تنقّل واحد', () => {
    renderAt('/account');
    // كلمة المرور قسم رابع منذ M9 (TD-29): صفحةٌ لا لوحةٌ في /account، لأن تلك الصفحة قُسِّمت في
    // المرحلة 16 لأنها كانت تحمل ثلاثة اهتمامات — والترتيب هو ترتيب القراءة في القشرة.
    expect(navLinks()).toEqual(['/account', '/account/addresses', '/account/password', '/orders']);
  });

  it('يخفي المفضّلة حين تكون وحدتها معطّلة', () => {
    renderAt('/account');
    expect(navLinks()).not.toContain('/wishlist');
  });

  it('يظهرها حين تكون مفعّلة', () => {
    tenant.modules = ['wishlist'];
    renderAt('/account');
    expect(navLinks()).toContain('/wishlist');
  });

  it('يبرز القسم الحالي وحده', () => {
    renderAt('/orders');
    const current = [...screen.getByRole('navigation').querySelectorAll('a[aria-current="page"]')];
    expect(current.map((a) => a.getAttribute('href'))).toEqual(['/orders']);
  });

  it('الملف الشخصي لا يبقى مُبرَزاً داخل صفحة العناوين', () => {
    // NavLink بلا end تطابق كل ما يبدأ بـ/account — قسمان مُبرَزان معاً.
    renderAt('/account/addresses');
    const current = [...screen.getByRole('navigation').querySelectorAll('a[aria-current="page"]')];
    expect(current.map((a) => a.getAttribute('href'))).toEqual(['/account/addresses']);
  });
});
