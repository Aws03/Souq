import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useModule } from '../app/TenantProvider';
import Spinner from './common/Spinner';
import styles from './ProtectedRoute.module.css';

// ============================================================================
// حرّاس المسارات — الطبقة الأولى (تجربة المستخدم). ملاحظة مهمة: هذا ليس حماية
// أمنية حقيقية — الحماية الفعلية في الخادم الذي يرفض أي طلب غير مصرّح بـ 401/403.
// دور الحارس هنا: ألّا نُظهر للمستخدم شاشة سيرفضها الخادم أصلاً (توجيه مبكر مهذّب).
// أثناء استعادة الجلسة لا نحكم بعد: التوجيه المبكر كان سيطرد مستخدماً مسجّلاً عند تحديث الصفحة.
// المرحلة 15: حارس صلاحية لكل صفحة إدارة، وحارس وحدة لصفحات الوحدات الاختيارية (الخادم يرفضها بـ 404 ModuleDisabled)، وحارس
// منطقة المنصّة.
// ============================================================================
export function PagePending() {
  return <div className={styles.pending}><Spinner size={28} /></div>;
}

// يتطلّب تسجيل دخول فقط. غير المسجّل يُوجَّه للدخول مع تذكّر وجهته الأصلية.
export function ProtectedRoute({ children }) {
  const { isAuthenticated, loading } = useAuth();
  const location = useLocation();
  if (loading) return <PagePending />;
  if (!isAuthenticated)
    return <Navigate to="/login" replace state={{ from: location }} />;
  return children;
}

// لوحة المتجر: مدير أو موظّف متجر (صلاحية إدارة واحدة على الأقل). العميل يُعاد للمتجر.
export function AdminRoute({ children }) {
  const { isAuthenticated, canManageStore, loading } = useAuth();
  const location = useLocation();
  if (loading) return <PagePending />;
  if (!isAuthenticated)
    return <Navigate to="/login" replace state={{ from: location }} />;
  if (!canManageStore)
    return <Navigate to="/" replace />;
  return children;
}

// صفحة إدارة بصلاحيتها (الشريط الجانبي يخفي رابطها بالشرط نفسه) — رابط مباشر بلا صلاحية يعود للوحة.
export function RequirePermission({ permission, fallback = '/admin', children }) {
  const { can } = useAuth();
  return can(permission) ? children : <Navigate to={fallback} replace />;
}

// صفحة وحدة اختيارية معطّلة في المتجر ⇒ لا تُعرض.
export function RequireModule({ module, fallback = '/', children }) {
  return useModule(module) ? children : <Navigate to={fallback} replace />;
}

// منطقة المنصّة: حساب منصّة مسجّل على مضيفها (توكن المتجر لا يصلح هنا أصلاً).
export function PlatformRoute({ children }) {
  const { user, isAuthenticated, loading } = useAuth();
  const location = useLocation();
  if (loading) return <PagePending />;
  if (!isAuthenticated || user?.area !== 'Platform')
    return <Navigate to="/login" replace state={{ from: location }} />;
  return children;
}
