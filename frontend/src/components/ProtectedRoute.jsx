import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

// ============================================================================
// حرّاس المسارات — الطبقة الأولى (تجربة المستخدم). ملاحظة مهمة: هذا ليس حماية
// أمنية حقيقية — الحماية الفعلية في الخادم الذي يرفض أي طلب غير مصرّح بـ 401/403.
// دور الحارس هنا: ألّا نُظهر للمستخدم شاشة سيرفضها الخادم أصلاً (توجيه مبكر مهذّب).
// ============================================================================

// يتطلّب تسجيل دخول فقط. غير المسجّل يُوجَّه للدخول مع تذكّر وجهته الأصلية.
export function ProtectedRoute({ children }) {
  const { isAuthenticated } = useAuth();
  const location = useLocation();
  if (!isAuthenticated)
    return <Navigate to="/login" replace state={{ from: location }} />;
  return children;
}

// يتطلّب دور Admin. العميل المسجّل يُعاد للمتجر (مسموح له لكن ليس هنا).
export function AdminRoute({ children }) {
  const { isAuthenticated, isAdmin } = useAuth();
  const location = useLocation();
  if (!isAuthenticated)
    return <Navigate to="/login" replace state={{ from: location }} />;
  if (!isAdmin)
    return <Navigate to="/" replace />;
  return children;
}
