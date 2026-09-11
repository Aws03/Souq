import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './context/ToastContext';
import { TenantProvider } from './app/TenantProvider';
import App from './App';
import './i18n';

// الترتيب مقصود: TenantProvider أولاً (إعداد متجر المضيف: الهوية والعملة واللغات والوحدات — المرحلة 15)، ثم AuthProvider
// (المتجر والإدارة والدخول يحتاجون هوية المستخدم)، وToastProvider يتيح تنبيهات موحّدة، وBrowserRouter يفعّل التوجيه بالمسارات.
ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <ToastProvider>
        <TenantProvider>
          <AuthProvider>
            <App />
          </AuthProvider>
        </TenantProvider>
      </ToastProvider>
    </BrowserRouter>
  </React.StrictMode>
);
