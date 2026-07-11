import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import App from './App';

// AuthProvider يلفّ كل شيء (المتجر والإدارة والدخول يحتاجون هوية المستخدم)،
// وBrowserRouter يفعّل التوجيه الحقيقي بالمسارات.
ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </React.StrictMode>
);
