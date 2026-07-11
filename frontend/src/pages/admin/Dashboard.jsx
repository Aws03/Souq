import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';

// لوحة تحكّم الأدمن: ترحيب + إحصاءات حقيقية من الـ API (لا أرقام وهمية).
// إحصاءات الطلبات ستُضاف عند بناء نقطة طلبات الأدمن في المرحلة 3أ.
export default function Dashboard() {
  const { user } = useAuth();
  const [stats, setStats] = useState({ products: null, categories: null });
  const [error, setError] = useState(null);

  useEffect(() => {
    Promise.all([api.getProducts({ pageSize: 1 }), api.getCategories()])
      .then(([products, categories]) =>
        setStats({ products: products.totalCount, categories: categories.length }))
      .catch((e) => setError(e.message));
  }, []);

  const fmt = (v) => (v == null ? '…' : v);

  return (
    <div>
      <h2 className="admin-page-title">أهلاً، {user?.fullName || 'المدير'}</h2>
      <p className="admin-page-sub">نظرة سريعة على متجرك.</p>

      {error && <div className="auth-alert" style={{ marginTop: 16 }}>⚠ {error}</div>}

      <div className="stat-row">
        <div className="stat-tile">
          <div className="stat-value">{fmt(stats.products)}</div>
          <div className="stat-label">منتج معروض</div>
        </div>
        <div className="stat-tile">
          <div className="stat-value">{fmt(stats.categories)}</div>
          <div className="stat-label">فئة</div>
        </div>
      </div>

      <div className="admin-quick">
        <Link to="/admin/products" className="admin-quick-card">
          <b>إدارة المنتجات</b>
          <span>إضافة، تعديل، أو تعطيل المنتجات ورفع صورها.</span>
        </Link>
        <Link to="/admin/categories" className="admin-quick-card">
          <b>إدارة الفئات</b>
          <span>تنظيم فئات المتجر.</span>
        </Link>
        <Link to="/admin/orders" className="admin-quick-card">
          <b>الطلبات</b>
          <span>متابعة الطلبات وتحديث حالتها.</span>
        </Link>
      </div>
    </div>
  );
}
