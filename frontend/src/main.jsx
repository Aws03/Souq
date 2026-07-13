import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './context/ToastContext';
import App from './App';
import './i18n';
import './theme';

// AuthProvider يلفّ كل شيء (المتجر والإدارة والدخول يحتاجون هوية المستخدم)،
// وToastProvider يتيح تنبيهات موحّدة من أي مكوّن، وBrowserRouter يفعّل التوجيه الحقيقي بالمسارات.
ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <ToastProvider>
        <AuthProvider>
          <App />
        </AuthProvider>
      </ToastProvider>
    </BrowserRouter>
  </React.StrictMode>
);
