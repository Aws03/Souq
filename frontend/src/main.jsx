import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './context/ToastContext';
import { TenantProvider } from './app/TenantProvider';
import { QueryProvider } from './app/QueryProvider';
import App from './App';
import { i18nReady } from './i18n';

// الترتيب مقصود: TenantProvider أولاً (إعداد متجر المضيف: الهوية والعملة واللغات والوحدات — المرحلة 15)، ثم AuthProvider
// (المتجر والإدارة والدخول يحتاجون هوية المستخدم)، ثم QueryProvider — تحت AuthProvider عمداً كي يرى تبدّل الهوية
// فيمسح ذاكرة الاستعلامات عندها (المرحلة 16، ADR-0037)، وToastProvider يتيح تنبيهات موحّدة، وBrowserRouter يفعّل التوجيه.
// حزمة الترجمة أولاً: العرض قبلها يُظهر أسماء المفاتيح للحظة (المرحلة: تقسيم الترجمة
// لكل لغة). الانتظار استيراد حزمة محلّية لا رحلة شبكة.
i18nReady.then(() => ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <ToastProvider>
        <TenantProvider>
          <AuthProvider>
            <QueryProvider>
              <App />
            </QueryProvider>
          </AuthProvider>
        </TenantProvider>
      </ToastProvider>
    </BrowserRouter>
  </React.StrictMode>
));
