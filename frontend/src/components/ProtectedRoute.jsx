import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Spinner from './common/Spinner';
import styles from './ProtectedRoute.module.css';

// ============================================================================
// حرّاس المسارات — الطبقة الأولى (تجربة المستخدم). ملاحظة مهمة: هذا ليس حماية
// أمنية حقيقية — الحماية الفعلية في الخادم الذي يرفض أي طلب غير مصرّح بـ 401/403.
// دور الحارس هنا: ألّا نُظهر للمستخدم شاشة سيرفضها الخادم أصلاً (توجيه مبكر مهذّب).
// أثناء استعادة الجلسة لا نحكم بعد: التوجيه المبكر كان سيطرد مستخدماً مسجّلاً عند تحديث الصفحة.
// ============================================================================
function SessionPending() {
  return <div className={styles.pending}><Spinner size={28} /></div>;
}

// يتطلّب تسجيل دخول فقط. غير المسجّل يُوجَّه للدخول مع تذكّر وجهته الأصلية.
export function ProtectedRoute({ children }) {
  const { isAuthenticated, loading } = useAuth();
  const location = useLocation();
  if (loading) return <SessionPending />;
  if (!isAuthenticated)
    return <Navigate to="/login" replace state={{ from: location }} />;
  return children;
}

// لوحة المتجر: مدير أو موظّف متجر (صلاحية إدارة واحدة على الأقل). العميل يُعاد للمتجر.
export function AdminRoute({ children }) {
  const { isAuthenticated, canManageStore, loading } = useAuth();
  const location = useLocation();
  if (loading) return <SessionPending />;
  if (!isAuthenticated)
    return <Navigate to="/login" replace state={{ from: location }} />;
  if (!canManageStore)
    return <Navigate to="/" replace />;
  return children;
}
