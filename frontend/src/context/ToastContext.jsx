import { createContext, useCallback, useContext, useRef, useState } from 'react';

// ============================================================================
// ToastContext — تنبيهات عابرة موحّدة لكل التطبيق (نجاح/خطأ/معلومة)، بديل
// alert() المتصفح القبيح. تظهر أسفل يسار الشاشة وتختفي تلقائياً بعد 3 ثوانٍ.
// ============================================================================
const ToastContext = createContext();
const AUTO_DISMISS_MS = 3000;

export function ToastProvider({ children }) {
  const [toasts, setToasts] = useState([]);
  const idRef = useRef(0);

  const dismiss = useCallback((id) => {
    setToasts((list) => list.filter((t) => t.id !== id));
  }, []);

  const notify = useCallback((message, variant = 'success') => {
    const id = ++idRef.current;
    setToasts((list) => [...list, { id, message, variant }]);
    setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
  }, [dismiss]);

  const value = {
    toasts,
    dismiss,
    success: (m) => notify(m, 'success'),
    error: (m) => notify(m, 'error'),
  };

  return <ToastContext.Provider value={value}>{children}</ToastContext.Provider>;
}

export const useToast = () => useContext(ToastContext);
