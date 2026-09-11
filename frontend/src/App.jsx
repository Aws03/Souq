import { useState, useEffect } from 'react';
import { Routes, Route, Outlet, Navigate, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { CartProvider } from './context/CartContext';
import { WishlistProvider } from './context/WishlistContext';
import { useToast } from './context/ToastContext';
import { api } from './api/client';
import { ProtectedRoute, AdminRoute } from './components/ProtectedRoute';
import AnnouncementBar from './components/layout/AnnouncementBar';
import Navbar from './components/layout/Navbar';
import CategoryNav from './components/layout/CategoryNav';
import Footer from './components/layout/Footer';
import CartDrawer from './components/cart/CartDrawer';
import ToastContainer from './components/common/ToastContainer';
import Store from './pages/Store';
import Offers from './pages/Offers';
import ProductDetail from './pages/ProductDetail';
import Wishlist from './pages/Wishlist';
import MyOrders from './pages/MyOrders';
import Account from './pages/account/Account';
import OrderTracking from './pages/OrderTracking';
import OrderDetail from './pages/OrderDetail';
import Checkout from './pages/checkout/Checkout';
import Confirmation from './pages/Confirmation';
import Login from './pages/auth/Login';
import Register from './pages/auth/Register';
import ForgotPassword from './pages/auth/ForgotPassword';
import ResetPassword from './pages/auth/ResetPassword';
import VerifyEmail from './pages/auth/VerifyEmail';
import AdminLayout from './pages/admin/AdminLayout';
import Dashboard from './pages/admin/Dashboard';
import Products from './pages/admin/Products';
import Inventory from './pages/admin/Inventory';
import Categories from './pages/admin/Categories';
import Coupons from './pages/admin/Coupons';
import Orders from './pages/admin/Orders';
import Customers from './pages/admin/Customers';
import Payments from './pages/admin/Payments';
import ShippingMethods from './pages/admin/ShippingMethods';
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
  const [categories, setCategories] = useState([]);
  const navigate = useNavigate();
  const toast = useToast();

  const showToast = (name) => toast.success(t('cart.added', { name }));
  const refreshProducts = () => setRefreshKey((k) => k + 1);

  // الفئات تُجلب مرّة واحدة هنا (تخطيط المتجر) وتُشارَك مع شريط الفئات وكل
  // الصفحات عبر سياق الـ Outlet — بدل جلبها في كل صفحة على حدة.
  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  return (
    <CartProvider>
      <WishlistProvider>
        <AnnouncementBar />
        <Navbar onCartClick={() => setDrawerOpen(true)} searchTerm={searchTerm} onSearchChange={setSearchTerm} />
        <CategoryNav categories={categories} />
        <Outlet context={{ showToast, refreshProducts, refreshKey, searchTerm, categories }} />
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
        <Route path="/forgot-password" element={<ForgotPassword />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        <Route path="/verify-email" element={<VerifyEmail />} />
        <Route path="/accept-invitation" element={<ResetPassword mode="invitation" />} />

        {/* ── لوحة المتجر (مدير/موظّف) — تخطيط منفصل ── */}
        <Route path="/admin" element={<AdminRoute><AdminLayout /></AdminRoute>}>
          <Route index element={<Dashboard />} />
          <Route path="products" element={<Products />} />
          <Route path="inventory" element={<Inventory />} />
          <Route path="categories" element={<Categories />} />
          <Route path="coupons" element={<Coupons />} />
          <Route path="orders" element={<Orders />} />
          <Route path="customers" element={<Customers />} />
          <Route path="shipping" element={<ShippingMethods />} />
          <Route path="payments" element={<Payments />} />
        </Route>

        {/* ── المتجر (عميل/زائر) ── */}
        <Route element={<CustomerLayout />}>
          <Route index element={<Store />} />
          <Route path="/offers" element={<Offers />} />
          <Route path="/products/:id" element={<ProductDetail />} />
          <Route path="/wishlist" element={<Wishlist />} />
          <Route path="/orders" element={<ProtectedRoute><MyOrders /></ProtectedRoute>} />
          <Route path="/account" element={<ProtectedRoute><Account /></ProtectedRoute>} />
          <Route path="/orders/:id" element={<ProtectedRoute><OrderDetail /></ProtectedRoute>} />
          {/* بلا حارس عمداً: رابط التتبّع العام بالرمز العشوائي (المرحلة 9، الخادم لا يتطلّب مصادقة لهذه النقطة) —
              يعمل لزائر لم يُسجّل الدخول أيضاً، ولا يُخمَّن رابط طلب آخر. */}
          <Route path="/track/:token" element={<OrderTracking />} />
          <Route path="/checkout" element={<ProtectedRoute><Checkout /></ProtectedRoute>} />
          <Route path="/confirmation" element={<ProtectedRoute><Confirmation /></ProtectedRoute>} />
        </Route>

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      <ToastContainer />
    </>
  );
}
