// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';

// ============================================================================
// حرّاس المسارات (TD-32 يسمّيها أول ما يُغطّى). الحارس ليس حماية — الخادم يرفض غير المصرّح —
// لكنه العقد الذي يقرّر ما يراه الزائر، وسلوكه الخاطئ يطرد مستخدماً مسجّلاً أو يعرض شاشة
// سيرفضها الخادم. أهمّ حالاته هي الثالثة: أثناء استعادة الجلسة لا يحكم الحارس بعد.
// ============================================================================
const auth = vi.hoisted(() => ({ value: {} }));
const tenant = vi.hoisted(() => ({ modules: [] }));

vi.mock('../context/AuthContext', () => ({ useAuth: () => auth.value }));
vi.mock('../app/TenantProvider', () => ({ useModule: (m) => tenant.modules.includes(m) }));

const { ProtectedRoute, AdminRoute, RequirePermission, RequireModule, PlatformRoute } =
  await import('./ProtectedRoute');

function renderAt(path, element) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path={path} element={element} />
        <Route path="/login" element={<p>login page</p>} />
        <Route path="/" element={<p>storefront</p>} />
        <Route path="/admin" element={<p>admin home</p>} />
      </Routes>
    </MemoryRouter>
  );
}

const Secret = () => <p>secret content</p>;

beforeEach(() => {
  auth.value = { isAuthenticated: false, loading: false, canManageStore: false, can: () => false, user: null };
  tenant.modules = [];
});

describe('ProtectedRoute', () => {
  it('لا يحكم أثناء استعادة الجلسة', () => {
    // الحالة التي تطرد مستخدماً مسجّلاً عند تحديث الصفحة لو أُسيء التعامل معها.
    auth.value = { ...auth.value, loading: true };
    renderAt('/account', <ProtectedRoute><Secret /></ProtectedRoute>);

    expect(screen.queryByText('login page')).toBeNull();
    expect(screen.queryByText('secret content')).toBeNull();
  });

  it('يوجّه الزائر إلى الدخول', () => {
    renderAt('/account', <ProtectedRoute><Secret /></ProtectedRoute>);
    expect(screen.getByText('login page')).toBeInTheDocument();
  });

  it('يعرض المحتوى للمسجّل', () => {
    auth.value = { ...auth.value, isAuthenticated: true };
    renderAt('/account', <ProtectedRoute><Secret /></ProtectedRoute>);
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });
});

describe('AdminRoute', () => {
  it('عميل مسجّل بلا صلاحية إدارة يعود للمتجر لا إلى الدخول', () => {
    auth.value = { ...auth.value, isAuthenticated: true, canManageStore: false };
    renderAt('/admin-area', <AdminRoute><Secret /></AdminRoute>);
    expect(screen.getByText('storefront')).toBeInTheDocument();
  });

  it('مدير المتجر يدخل', () => {
    auth.value = { ...auth.value, isAuthenticated: true, canManageStore: true };
    renderAt('/admin-area', <AdminRoute><Secret /></AdminRoute>);
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });
});

describe('RequirePermission and RequireModule', () => {
  it('بلا صلاحية يعود للوحة', () => {
    renderAt('/admin-page', <RequirePermission permission="catalog.manage"><Secret /></RequirePermission>);
    expect(screen.getByText('admin home')).toBeInTheDocument();
  });

  it('بالصلاحية يُعرض', () => {
    auth.value = { ...auth.value, can: (p) => p === 'catalog.manage' };
    renderAt('/admin-page', <RequirePermission permission="catalog.manage"><Secret /></RequirePermission>);
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });

  it('وحدة معطّلة في المتجر لا تُعرض صفحتها', () => {
    renderAt('/wishlist', <RequireModule module="wishlist"><Secret /></RequireModule>);
    expect(screen.getByText('storefront')).toBeInTheDocument();
  });

  it('وحدة مفعّلة تُعرض', () => {
    tenant.modules = ['wishlist'];
    renderAt('/wishlist', <RequireModule module="wishlist"><Secret /></RequireModule>);
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });
});

describe('PlatformRoute', () => {
  it('حساب متجر لا يدخل منطقة المنصّة', () => {
    // عزل المناطق: توكن متجر لا يصلح على مضيف المنصّة، والخادم يرفضه أيضاً.
    auth.value = { ...auth.value, isAuthenticated: true, user: { area: 'Tenant' } };
    renderAt('/platform', <PlatformRoute><Secret /></PlatformRoute>);
    expect(screen.getByText('login page')).toBeInTheDocument();
  });

  it('حساب منصّة يدخل', () => {
    auth.value = { ...auth.value, isAuthenticated: true, user: { area: 'Platform' } };
    renderAt('/platform', <PlatformRoute><Secret /></PlatformRoute>);
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });
});
