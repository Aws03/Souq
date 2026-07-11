import { useState, useEffect } from 'react';
import { Routes, Route, Outlet, Navigate, useNavigate } from 'react-router-dom';
import { CartProvider } from './context/CartContext';
import { ProtectedRoute, AdminRoute } from './components/ProtectedRoute';
import Navbar from './components/Navbar';
import CartDrawer from './components/CartDrawer';
import Store from './pages/Store';
import Checkout from './pages/Checkout';
import Confirmation from './pages/Confirmation';
import Login from './pages/Login';
import Register from './pages/Register';
import AdminLayout from './pages/admin/AdminLayout';
import Dashboard from './pages/admin/Dashboard';
import AdminSection from './pages/admin/AdminSection';
import './styles.css';

// ============================================================================
// تخطيط المتجر (العميل/الزائر): يغلّف السلة + شريط التنقّل + درج السلة + التنبيه،
// وتُعرَض الصفحات داخله عبر <Outlet>. منفصل تماماً عن تخطيط الأدمن (AdminLayout)
// — لكل دور تجربته الخاصة، لا صفحة واحدة بأزرار مخفية.
// ============================================================================
function CustomerLayout() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [toast, setToast] = useState(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const navigate = useNavigate();

  const showToast = (name) => setToast(`أُضيف "${name}" للسلة`);
  const refreshProducts = () => setRefreshKey((k) => k + 1);
  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 1800);
    return () => clearTimeout(t);
  }, [toast]);

  return (
    <CartProvider>
      <Navbar onCartClick={() => setDrawerOpen(true)} />
      <Outlet context={{ showToast, refreshProducts, refreshKey }} />
      <CartDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)}
        onCheckout={() => { setDrawerOpen(false); navigate('/checkout'); }} />
      {toast && <div className="toast">{toast}</div>}
    </CartProvider>
  );
}

export default function App() {
  return (
    <Routes>
      {/* ── المصادقة ── */}
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />

      {/* ── لوحة الإدارة (Admin فقط) — تخطيط منفصل ── */}
      <Route path="/admin" element={<AdminRoute><AdminLayout /></AdminRoute>}>
        <Route index element={<Dashboard />} />
        <Route path="products" element={<AdminSection title="إدارة المنتجات" />} />
        <Route path="categories" element={<AdminSection title="إدارة الفئات" />} />
        <Route path="orders" element={<AdminSection title="الطلبات" />} />
      </Route>

      {/* ── المتجر (عميل/زائر) ── */}
      <Route element={<CustomerLayout />}>
        <Route index element={<Store />} />
        <Route path="/checkout" element={<ProtectedRoute><Checkout /></ProtectedRoute>} />
        <Route path="/confirmation" element={<ProtectedRoute><Confirmation /></ProtectedRoute>} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
