import { useState, useEffect } from 'react';
import { CartProvider } from './context/CartContext';
import Navbar from './components/Navbar';
import CartDrawer from './components/CartDrawer';
import Storefront from './pages/Storefront';
import Checkout from './pages/Checkout';
import Confirmation from './pages/Confirmation';
import { api } from './api/client';
import { useProducts } from './hooks/useProducts';
import './styles.css';

export default function App() {
  const [view, setView] = useState('store');       // store | checkout | confirm
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [filter, setFilter] = useState(null);
  const [toast, setToast] = useState(null);
  const [order, setOrder] = useState(null);
  const [categories, setCategories] = useState([]);
  const [refreshKey, setRefreshKey] = useState(0);

  // المنتجات من الـ API الحقيقي — تُعاد عند تغيير الفئة أو بعد إتمام طلب
  // (كي يظهر المخزون الجديد كما هو في قاعدة البيانات).
  const { products, loading, error } = useProducts({ categoryId: filter, refreshKey });

  // الفئات تُجلب مرة واحدة عند الإقلاع.
  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  const showToast = (name) => { setToast(`أُضيف "${name}" للسلة`); };
  useEffect(() => { if (toast) { const t = setTimeout(() => setToast(null), 1800); return () => clearTimeout(t); } }, [toast]);

  return (
    <CartProvider>
      <Navbar onCartClick={() => setDrawerOpen(true)} />

      {view === 'store' && (
        <Storefront products={products} categories={categories} loading={loading} error={error}
          filter={filter} setFilter={setFilter} onAdded={showToast} />
      )}
      {view === 'checkout' && (
        <Checkout onBack={() => setView('store')}
          onPlaced={(o) => { setOrder(o); setRefreshKey((k) => k + 1); setView('confirm'); }} />
      )}
      {view === 'confirm' && order && (
        <Confirmation order={order} onContinue={() => { setOrder(null); setView('store'); }} />
      )}

      <CartDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)}
        onCheckout={() => { setDrawerOpen(false); setView('checkout'); }} />

      {toast && <div className="toast">{toast}</div>}
    </CartProvider>
  );
}
