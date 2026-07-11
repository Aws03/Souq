import { useState } from 'react';
import { Routes, Route, Outlet, Navigate, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { CartProvider } from './context/CartContext';
import { WishlistProvider } from './context/WishlistContext';
import { useToast } from './context/ToastContext';
import { ProtectedRoute, AdminRoute } from './components/ProtectedRoute';
import AnnouncementBar from './components/layout/AnnouncementBar';
import Navbar from './components/layout/Navbar';
import Footer from './components/layout/Footer';
import CartDrawer from './components/cart/CartDrawer';
import ToastContainer from './components/common/ToastContainer';
import Store from './pages/Store';
import ProductDetail from './pages/ProductDetail';
import Wishlist from './pages/Wishlist';
import Checkout from './pages/checkout/Checkout';
import Confirmation from './pages/Confirmation';
import Login from './pages/auth/Login';
import Register from './pages/auth/Register';
import AdminLayout from './pages/admin/AdminLayout';
import Dashboard from './pages/admin/Dashboard';
import Products from './pages/admin/Products';
import Categories from './pages/admin/Categories';
import Coupons from './pages/admin/Coupons';
import Orders from './pages/admin/Orders';
import './styles.css';

// ============================================================================
// تخطيط المتجر (العميل/الزائر): يغلّف السلة + المفضّلة + شريط الإعلان + شريط
// التنقّل + تذييل الصفحة، وتُعرَض الصفحات داخله عبر <Outlet>. منفصل تماماً عن
// تخطيط الأدمن (AdminLayout) — لكل دور تجربته الخاصة، لا صفحة واحدة بأزرار مخفية.
// ============================================================================
function CustomerLayout() {
  const { t } = useTranslation();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [searchTerm, setSearchTerm] = useState('');
  const navigate = useNavigate();
  const toast = useToast();

  const showToast = (name) => toast.success(t('cart.added', { name }));
  const refreshProducts = () => setRefreshKey((k) => k + 1);

  return (
    <CartProvider>
      <WishlistProvider>
        <AnnouncementBar />
        <Navbar onCartClick={() => setDrawerOpen(true)} searchTerm={searchTerm} onSearchChange={setSearchTerm} />
        <Outlet context={{ showToast, refreshProducts, refreshKey, searchTerm }} />
        <Footer />
        <CartDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)}
          onCheckout={() => { setDrawerOpen(false); navigate('/checkout'); }} />
      </WishlistProvider>
    </CartProvider>
  );
}

export default function App() {
  return (
    <>
      <Routes>
        {/* ── المصادقة ── */}
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />

        {/* ── لوحة الإدارة (Admin فقط) — تخطيط منفصل ── */}
        <Route path="/admin" element={<AdminRoute><AdminLayout /></AdminRoute>}>
          <Route index element={<Dashboard />} />
          <Route path="products" element={<Products />} />
          <Route path="categories" element={<Categories />} />
          <Route path="coupons" element={<Coupons />} />
          <Route path="orders" element={<Orders />} />
        </Route>

        {/* ── المتجر (عميل/زائر) ── */}
        <Route element={<CustomerLayout />}>
          <Route index element={<Store />} />
          <Route path="/products/:id" element={<ProductDetail />} />
          <Route path="/wishlist" element={<Wishlist />} />
          <Route path="/checkout" element={<ProtectedRoute><Checkout /></ProtectedRoute>} />
          <Route path="/confirmation" element={<ProtectedRoute><Confirmation /></ProtectedRoute>} />
        </Route>

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      <ToastContainer />
    </>
  );
}
