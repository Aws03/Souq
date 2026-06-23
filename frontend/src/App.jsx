import { useState, useEffect } from 'react';
import { CartProvider } from './context/CartContext';
import Navbar from './components/Navbar';
import CartDrawer from './components/CartDrawer';
import Storefront from './pages/Storefront';
import Checkout from './pages/Checkout';
import Confirmation from './pages/Confirmation';
import './styles.css';

// بيانات تجريبية (في النسخة الكاملة تأتي من api.getProducts/getCategories).
const SEED_CATEGORIES = [
  { id: 1, name: 'إلكترونيات' }, { id: 2, name: 'أزياء' }, { id: 3, name: 'منزل' },
];
const SEED_PRODUCTS = [
  { id: 1, name: 'سمّاعات لاسلكية', description: 'صوت نقي وعزل ضوضاء فعّال', price: 59.9, currency: 'JOD', stockQuantity: 25, emoji: '🎧', categoryId: 1, categoryName: 'إلكترونيات' },
  { id: 2, name: 'ساعة ذكية', description: 'تتبّع اللياقة والإشعارات', price: 120, currency: 'JOD', stockQuantity: 4, emoji: '⌚', categoryId: 1, categoryName: 'إلكترونيات' },
  { id: 3, name: 'لوحة مفاتيح ميكانيكية', description: 'إضاءة خلفية ومفاتيح مريحة', price: 45.5, currency: 'JOD', stockQuantity: 30, emoji: '⌨️', categoryId: 1, categoryName: 'إلكترونيات' },
  { id: 4, name: 'حقيبة ظهر جلدية', description: 'تصميم أنيق ومتين للعمل والسفر', price: 35, currency: 'JOD', stockQuantity: 18, emoji: '🎒', categoryId: 2, categoryName: 'أزياء' },
  { id: 5, name: 'نظّارة شمسية', description: 'حماية UV وإطار خفيف', price: 22, currency: 'JOD', stockQuantity: 40, emoji: '🕶️', categoryId: 2, categoryName: 'أزياء' },
  { id: 6, name: 'مصباح مكتب LED', description: 'إضاءة قابلة للتعديل وموفّرة للطاقة', price: 18.75, currency: 'JOD', stockQuantity: 50, emoji: '💡', categoryId: 3, categoryName: 'منزل' },
  { id: 7, name: 'ركوة قهوة نحاسية', description: 'صناعة يدوية لقهوة عربية أصيلة', price: 28, currency: 'JOD', stockQuantity: 3, emoji: '☕', categoryId: 3, categoryName: 'منزل' },
  { id: 8, name: 'كوب حراري', description: 'يحفظ الحرارة 12 ساعة', price: 14.5, currency: 'JOD', stockQuantity: 60, emoji: '🥤', categoryId: 3, categoryName: 'منزل' },
];

export default function App() {
  const [view, setView] = useState('store');       // store | checkout | confirm
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [filter, setFilter] = useState(null);
  const [toast, setToast] = useState(null);
  const [order, setOrder] = useState(null);

  const products = filter ? SEED_PRODUCTS.filter((p) => p.categoryId === filter) : SEED_PRODUCTS;

  const showToast = (name) => { setToast(`أُضيف "${name}" للسلة`); };
  useEffect(() => { if (toast) { const t = setTimeout(() => setToast(null), 1800); return () => clearTimeout(t); } }, [toast]);

  return (
    <CartProvider>
      <Navbar onCartClick={() => setDrawerOpen(true)} />

      {view === 'store' && (
        <Storefront products={products} categories={SEED_CATEGORIES} loading={false}
          filter={filter} setFilter={setFilter} onAdded={showToast} />
      )}
      {view === 'checkout' && (
        <Checkout onBack={() => setView('store')}
          onPlaced={(o) => { setOrder(o); setView('confirm'); }} />
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
