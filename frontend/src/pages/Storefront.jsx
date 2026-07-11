import { useState } from 'react';
import ProductCard from '../components/ProductCard';

// صفحة المتجر: البحث + التصفية بالفئة + شبكة المنتجات.
// (البحث محلي على الصفحة المعروضة مؤقتاً — يتصل بالـ API في المرحلة 3 مع الترقيم.)
export default function Storefront({ products, categories, loading, error, filter, setFilter, onAdded }) {
  const [search, setSearch] = useState('');
  const visible = products.filter((p) =>
    p.name.includes(search) || p.description.includes(search));

  return (
    <>
      <section className="hero">
        <h1>سوقك في <b>جيبك</b></h1>
        <p>منتجات منتقاة بعناية — من الإلكترونيات إلى صناعات يدوية أصيلة.</p>
      </section>
      <div className="layout">
        <div className="toolbar">
          <input className="search" placeholder="ابحث عن منتج…"
            value={search} onChange={(e) => setSearch(e.target.value)} />
          <button className={`chip ${!filter ? 'active' : ''}`} onClick={() => setFilter(null)}>الكل</button>
          {categories.map((c) => (
            <button key={c.id} className={`chip ${filter === c.id ? 'active' : ''}`}
              onClick={() => setFilter(c.id)}>{c.name}</button>
          ))}
        </div>
        {error ? (
          <div className="empty">⚠ {error}</div>
        ) : loading ? (
          <div className="empty">جارٍ التحميل…</div>
        ) : visible.length === 0 ? (
          <div className="empty">لا منتجات مطابقة</div>
        ) : (
          <div className="grid">
            {visible.map((p) => <ProductCard key={p.id} product={p} onAdded={onAdded} />)}
          </div>
        )}
      </div>
    </>
  );
}
