import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { api, tokenStore } from '../api/client';

// ============================================================================
// AuthContext — مصدر الحقيقة لهوية المستخدم في الواجهة (مسجّل؟ ما دوره؟).
// نفصل "من المستخدم" عن كل مكوّن يحتاجه (Navbar، حرّاس المسارات، صفحة الدفع...)
// عبر Context واحد، بدل تمرير الحالة يدوياً. التوكن يُخزَّن عبر tokenStore،
// وبيانات المستخدم الأساسية (id/name/email/role) في localStorage أيضاً كي
// تبقى الجلسة بعد تحديث الصفحة دون طلب إضافي للخادم.
// ============================================================================
const AuthContext = createContext();
const USER_KEY = 'souq_user';

function loadStoredUser() {
  // نثق بالمستخدم المخزّن فقط إن وُجد توكن مقابل له (الخادم هو الحكم الفعلي:
  // أي طلب محمي بتوكن فاسد يُرفض ويُسجّل الخروج تلقائياً).
  if (!tokenStore.get()) return null;
  try { return JSON.parse(localStorage.getItem(USER_KEY)); }
  catch { return null; }
}

export function AuthProvider({ children }) {
  const [user, setUser] = useState(loadStoredUser);

  const applySession = useCallback((auth) => {
    tokenStore.set(auth.token);
    localStorage.setItem(USER_KEY, JSON.stringify(auth.user));
    setUser(auth.user);
    return auth.user;   // نعيده كي يوجّه المتصل حسب الدور فوراً
  }, []);

  const logout = useCallback(() => {
    tokenStore.clear();
    localStorage.removeItem(USER_KEY);
    setUser(null);
  }, []);

  const login = useCallback(async (email, password) =>
    applySession(await api.login({ email, password })), [applySession]);

  const register = useCallback(async (fullName, email, password) =>
    applySession(await api.register({ fullName, email, password })), [applySession]);

  // توكن رفضه الخادم (401) ⇒ ننهي الجلسة محلياً كي تتّسق الواجهة مع الواقع.
  useEffect(() => {
    const onUnauthorized = () => logout();
    window.addEventListener('auth:unauthorized', onUnauthorized);
    return () => window.removeEventListener('auth:unauthorized', onUnauthorized);
  }, [logout]);

  const value = {
    user,
    isAuthenticated: !!user,
    isAdmin: user?.role === 'Admin',
    login, register, logout,
  };
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);
