import { lazy, Suspense, useState } from 'react';
import { Routes, Route, Outlet, Navigate, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { CartProvider } from './context/CartContext';
import { WishlistProvider } from './context/WishlistContext';
import { useToast } from './context/ToastContext';
import { useTenant } from './app/TenantProvider';
import { api } from './api/client';
import { queryKeys } from './app/queryKeys';
import {
  AdminRoute, PagePending, PlatformRoute, ProtectedRoute, RequireModule, RequirePermission,
} from './components/ProtectedRoute';
import AnnouncementBar from './components/layout/AnnouncementBar';
import Navbar from './components/layout/Navbar';
import CategoryNav from './components/layout/CategoryNav';
import Footer from './components/layout/Footer';
import CartDrawer from './components/cart/CartDrawer';
import ToastContainer from './components/common/ToastContainer';
import ErrorBoundary from './components/common/ErrorBoundary';
import Store from './pages/Store';
import NotFound from './pages/NotFound';
import ProductDetail from './pages/ProductDetail';
import './styles.css';

// ============================================================================
// أربع مناطق ببناء واحد (المرحلة 15، FrontendArchitecture.md §2): واجهة المتجر، حساب العميل (/account، /orders)، لوحة المتجر
// (/admin)، ومنطقة المنصّة (مضيف المنصّة). المنطقة من إعداد المضيف (TenantProvider)، ولكل منطقة تخطيطها وحرّاسها — والحرّاس
// تجربة لا حماية: الخادم يفرض الصلاحيات والوحدات والمتجر. تقسيم الشيفرة على المسارات: الزائر لا يحمّل حزم الإدارة ولا الدفع ولا
// المنصّة — الرئيسية وصفحة المنتج وحدهما في الحزمة الأولى.
// ============================================================================
const Offers = lazy(() => import('./pages/Offers'));
const Wishlist = lazy(() => import('./pages/Wishlist'));
const Cart = lazy(() => import('./pages/Cart'));
const MyOrders = lazy(() => import('./pages/MyOrders'));
const AccountLayout = lazy(() => import('./pages/account/AccountLayout'));
const Profile = lazy(() => import('./pages/account/Profile'));
const Addresses = lazy(() => import('./pages/account/Addresses'));
const OrderTracking = lazy(() => import('./pages/OrderTracking'));
const OrderDetail = lazy(() => import('./pages/OrderDetail'));
const Checkout = lazy(() => import('./pages/checkout/Checkout'));
const Confirmation = lazy(() => import('./pages/Confirmation'));
const Login = lazy(() => import('./pages/auth/Login'));
const Register = lazy(() => import('./pages/auth/Register'));
const ForgotPassword = lazy(() => import('./pages/auth/ForgotPassword'));
const ResetPassword = lazy(() => import('./pages/auth/ResetPassword'));
const VerifyEmail = lazy(() => import('./pages/auth/VerifyEmail'));
const AdminLayout = lazy(() => import('./pages/admin/AdminLayout'));
const Dashboard = lazy(() => import('./pages/admin/Dashboard'));
const BusinessOverview = lazy(() => import('./pages/admin/BusinessOverview'));
const Products = lazy(() => import('./pages/admin/Products'));
const Inventory = lazy(() => import('./pages/admin/Inventory'));
const Categories = lazy(() => import('./pages/admin/Categories'));
const Coupons = lazy(() => import('./pages/admin/Coupons'));
const Orders = lazy(() => import('./pages/admin/Orders'));
const Customers = lazy(() => import('./pages/admin/Customers'));
const Payments = lazy(() => import('./pages/admin/Payments'));
const ShippingMethods = lazy(() => import('./pages/admin/ShippingMethods'));
const ReviewModeration = lazy(() => import('./pages/admin/ReviewModeration'));
const PlatformLayout = lazy(() => import('./app/PlatformLayout'));

// صفحة إدارة بصلاحيتها (والوحدة إن كانت اختيارية) — الشريط الجانبي يخفي رابطها بالشرط نفسه.
const guarded = (element, permission, module) => {
  const page = <RequirePermission permission={permission}>{element}</RequirePermission>;
  return module ? <RequireModule module={module} fallback="/admin">{page}</RequireModule> : page;
};

// ============================================================================
// تخطيط المتجر (العميل/الزائر): يغلّف السلة + المفضّلة + شريط الإعلان + شريط
// التنقّل + تذييل الصفحة، وتُعرَض الصفحات داخله عبر <Outlet>. منفصل تماماً عن
// تخطيط الأدمن (AdminLayout) — لكل دور تجربته الخاصة، لا صفحة واحدة بأزرار مخفية.
// ============================================================================
function CustomerLayout() {
  const { t } = useTranslation();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const navigate = useNavigate();
  const toast = useToast();

  const showToast = (name) => toast.success(t('cart.added', { name }));
  const refreshProducts = () => setRefreshKey((k) => k + 1);

  // الفئات تُجلب مرّة واحدة هنا (تخطيط المتجر) وتُشارَك مع شريط الفئات وكل الصفحات عبر سياق
  // الـ Outlet — بدل جلبها في كل صفحة على حدة. شجرة الفئات تتغيّر نادراً (تعديل إداري)، فلها
  // مهلة طزاجة صريحة: خمس دقائق بلا إعادة سؤال، على خلاف الافتراض في بقية المتجر.
  const { data: categories = [] } = useQuery({
    queryKey: queryKeys.categories(),
    queryFn: api.getCategories,
    staleTime: 5 * 60 * 1000,
  });

  return (
    <CartProvider>
      <WishlistProvider>
        <AnnouncementBar />
        <Navbar onCartClick={() => setDrawerOpen(true)} />
        <CategoryNav categories={categories} />
        <Suspense fallback={<PagePending />}>
          <Outlet context={{ showToast, refreshProducts, refreshKey, categories }} />
        </Suspense>
        <Footer />
        <CartDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)}
          onCheckout={() => { setDrawerOpen(false); navigate('/checkout'); }} />
      </WishlistProvider>
    </CartProvider>
  );
}

function StoreRoutes() {
  return (
    <Routes>
      {/* ── المصادقة ── */}
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />
      <Route path="/forgot-password" element={<ForgotPassword />} />
      <Route path="/reset-password" element={<ResetPassword />} />
      <Route path="/verify-email" element={<VerifyEmail />} />
      <Route path="/accept-invitation" element={<ResetPassword mode="invitation" />} />

      {/* ── لوحة المتجر (مدير/موظّف) — تخطيط منفصل، وكل صفحة بصلاحيتها ── */}
      <Route path="/admin" element={<AdminRoute><AdminLayout /></AdminRoute>}>
        <Route index element={<Dashboard />} />
        {/* نظرة العمل: نفس البيانات ونفس الصلاحية، وسؤال مختلف — لمالك أو شريك لا لمن يشغّل المتجر. */}
        <Route path="business" element={guarded(<BusinessOverview />, 'store.reports.view')} />
        <Route path="products" element={guarded(<Products />, 'catalog.manage')} />
        <Route path="inventory" element={guarded(<Inventory />, 'inventory.view')} />
        <Route path="categories" element={guarded(<Categories />, 'catalog.manage')} />
        <Route path="coupons" element={guarded(<Coupons />, 'promotions.manage', 'promotions')} />
        <Route path="orders" element={guarded(<Orders />, 'orders.view')} />
        <Route path="customers" element={guarded(<Customers />, 'customers.view')} />
        <Route path="shipping" element={guarded(<ShippingMethods />, 'store.shipping.manage')} />
        <Route path="payments" element={guarded(<Payments />, 'store.payments.manage')} />
        <Route path="reviews" element={guarded(<ReviewModeration />, 'reviews.moderate', 'reviews')} />
      </Route>

      {/* ── المتجر (عميل/زائر) وحساب العميل ── */}
      <Route element={<CustomerLayout />}>
        <Route index element={<Store />} />
        <Route path="/offers" element={<Offers />} />
        {/* المقبض قد يكون الاسم (القانوني) أو المعرّف — روابط قديمة تبقى تعمل وتُحوَّل. */}
        <Route path="/products/:handle" element={<ProductDetail />} />
        <Route path="/wishlist" element={<RequireModule module="wishlist"><Wishlist /></RequireModule>} />
        {/* السلة بصفحتها إلى جانب الدرج — بلا حارس: الزائر له سلة أيضاً (ملف تعريف ارتباط HttpOnly من الخادم). */}
        <Route path="/cart" element={<Cart />} />

        {/* قشرة حساب العميل (المرحلة 16): الملف والعناوين والطلبات في منطقة واحدة بتنقّل واحد.
            /orders و/orders/:id لم تتغيّر — روابط البريد والإشعارات تشير إليهما. صفحة الطلب
            الواحد تبقى خارج القشرة: لها رجوعها الخاص وعرضها العريض، لا قائمة جانبية بجانبه. */}
        <Route element={<ProtectedRoute><AccountLayout /></ProtectedRoute>}>
          <Route path="/account" element={<Profile />} />
          <Route path="/account/addresses" element={<Addresses />} />
          <Route path="/orders" element={<MyOrders />} />
        </Route>
        <Route path="/orders/:id" element={<ProtectedRoute><OrderDetail /></ProtectedRoute>} />
        {/* بلا حارس عمداً: رابط التتبّع العام بالرمز العشوائي (المرحلة 9، الخادم لا يتطلّب مصادقة لهذه النقطة) —
            يعمل لزائر لم يُسجّل الدخول أيضاً، ولا يُخمَّن رابط طلب آخر. */}
        <Route path="/track/:token" element={<OrderTracking />} />
        <Route path="/checkout" element={<ProtectedRoute><Checkout /></ProtectedRoute>} />
        <Route path="/confirmation" element={<ProtectedRoute><Confirmation /></ProtectedRoute>} />
        {/* مسار مجهول داخل تخطيط المتجر: 404 صريحة مع إبقاء التنقّل والسلّة في متناول الزائر.
            التحويل الصامت للرئيسية كان يُخفي الروابط المكسورة عن الزائر وعن محرّكات البحث معاً. */}
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}

// منطقة المنصّة على مضيفها: الدخول وقبول الدعوات، ولوحتها لحسابات المنصّة (شاشاتها في المرحلة 18).
function PlatformRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/forgot-password" element={<ForgotPassword />} />
      <Route path="/reset-password" element={<ResetPassword />} />
      <Route path="/accept-invitation" element={<ResetPassword mode="invitation" />} />
      <Route path="/platform/*" element={<PlatformRoute><PlatformLayout /></PlatformRoute>} />
      <Route path="*" element={<Navigate to="/platform" replace />} />
    </Routes>
  );
}

export default function App() {
  const { mode } = useTenant();
  return (
    <>
      <ErrorBoundary>
        <Suspense fallback={<PagePending />}>
          {mode === 'platform' ? <PlatformRoutes /> : <StoreRoutes />}
        </Suspense>
      </ErrorBoundary>
      <ToastContainer />
    </>
  );
}
