import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';

// ============================================================================
// تخطيط لوحة الإدارة — منفصل فعلياً عن تخطيط المتجر (شريط جانبي + ترويسة خاصة)،
// لا هو صفحة المتجر بأزرار مخفية. العميل لا يصل هنا إطلاقاً (AdminRoute + الخادم).
// أقسام الإدارة (المنتجات/الفئات/الطلبات) تُملأ بنماذجها الكاملة في المرحلة 3ب،
// وتُعرَض هنا داخل <Outlet> عبر التوجيه المتداخل.
// ============================================================================
export default function AdminLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const doLogout = () => { logout(); navigate('/login', { replace: true }); };

  const link = ({ isActive }) => 'admin-nav-link' + (isActive ? ' active' : '');

  return (
    <div className="admin-shell">
      <aside className="admin-sidebar">
        <div className="admin-brand">سو<span>ق</span> · الإدارة</div>
        <nav className="admin-nav">
          {/* end: كي لا يبقى "لوحة التحكم" نشطاً على المسارات الفرعية */}
          <NavLink to="/admin" end className={link}>لوحة التحكم</NavLink>
          <NavLink to="/admin/products" className={link}>المنتجات</NavLink>
          <NavLink to="/admin/categories" className={link}>الفئات</NavLink>
          <NavLink to="/admin/orders" className={link}>الطلبات</NavLink>
        </nav>
        <div className="admin-sidebar-foot">
          <a href="/" className="admin-nav-link">← المتجر</a>
          <button className="admin-logout" onClick={doLogout}>تسجيل الخروج</button>
        </div>
      </aside>

      <div className="admin-main">
        <header className="admin-header">
          <span className="admin-header-title">لوحة تحكّم المتجر</span>
          <span className="admin-user">{user?.fullName || 'المدير'}</span>
        </header>
        <main className="admin-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
