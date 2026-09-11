import { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import { api, authEvents, refreshSession, SESSION_EXPIRED } from '../api/client';

// ============================================================================
// AuthContext — مصدر الحقيقة لهوية المستخدم في الواجهة (مسجّل؟ ماذا يستطيع؟). لا شيء في localStorage:
// عند الإقلاع نسأل الخادم بتجديد صامت (ملف تعريف الارتباط HttpOnly) — جلسة قائمة ⇒ المستخدم وتوكن في
// الذاكرة، وإلا فزائر. حتى تنتهي هذه الخطوة loading=true كي لا يُطرد مستخدم مسجّل إلى الدخول عند
// تحديث الصفحة. ما يظهر يُحدَّد بالصلاحيات القادمة من الخادم (user.permissions) لا باسم الدور.
// ============================================================================
const AuthContext = createContext(null);

// لوحة المتجر لحساب متجر يملك صلاحية إدارة واحدة على الأقل (مدير/موظّف). العميل بلا صلاحيات، وحساب
// المنصّة يُدار من مضيف المنصّة. الخادم يحرس كل نقطة بصلاحيتها في كل الأحوال.
export const canManageStore = (user) => user?.area === 'Store' && (user.permissions?.length ?? 0) > 0;

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  // استعادة الجلسة (تحديث الصفحة، تبويب جديد). الخادم غير متاح ⇒ زائر حتى المحاولة التالية.
  useEffect(() => {
    let active = true;
    refreshSession()
      .catch(() => null)
      .then((current) => {
        if (!active) return;
        setUser(current);
        setLoading(false);
      });
    return () => { active = false; };
  }, []);

  // الجلسة انتهت فعلاً (تجديد مرفوض) ⇒ الواجهة تتّسق مع الخادم: زائر، والحرّاس يوجّهون للدخول.
  useEffect(() => {
    const onExpired = () => setUser(null);
    authEvents.addEventListener(SESSION_EXPIRED, onExpired);
    return () => authEvents.removeEventListener(SESSION_EXPIRED, onExpired);
  }, []);

  const login = useCallback(async (email, password) => {
    const current = await api.login({ email, password });
    setUser(current);
    return current;   // نعيده كي يوجّه المتصل حسب الصلاحيات فوراً
  }, []);

  const register = useCallback(async (fullName, email, password) => {
    const current = await api.register({ fullName, email, password });
    setUser(current);
    return current;
  }, []);

  const changePassword = useCallback(async (currentPassword, newPassword) => {
    setUser(await api.changePassword(currentPassword, newPassword));
  }, []);

  const reloadUser = useCallback(async () => setUser(await api.me()), []);

  // الواجهة تخرج فوراً؛ إبطال رمز التجديد على الخادم بعدها (فشله لا يُبقي أحداً داخلاً هنا).
  const logout = useCallback(() => {
    setUser(null);
    api.logout().catch(() => {});
  }, []);

  const value = useMemo(() => ({
    user,
    loading,
    isAuthenticated: !!user,
    canManageStore: canManageStore(user),
    can: (permission) => !!user?.permissions?.includes(permission),
    login, register, changePassword, reloadUser, logout,
  }), [user, loading, login, register, changePassword, reloadUser, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);
