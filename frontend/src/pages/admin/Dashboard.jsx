import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import styles from './Admin.module.css';

// لوحة تحكّم الأدمن: ترحيب + إحصاءات حقيقية من الـ API (لا أرقام وهمية).
export default function Dashboard() {
  const { user } = useAuth();
  const [stats, setStats] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    Promise.all([api.getProducts({ pageSize: 1 }), api.getCategories()])
      .then(([products, categories]) => setStats({ products: products.totalCount, categories: categories.length }))
      .catch((e) => setError(e.message));
  }, []);

  return (
    <div>
      <h2 className={styles.pageTitle}>أهلاً، {user?.fullName || 'المدير'}</h2>
      <p className={styles.pageSub}>نظرة سريعة على متجرك.</p>

      {error && <ErrorBanner message={error} />}

      <div className={styles.statRow}>
        <div className={styles.statTile}>
          <div className={styles.statValue}>{stats ? stats.products : <Skeleton width={60} height={34} />}</div>
          <div className={styles.statLabel}>منتج معروض</div>
        </div>
        <div className={styles.statTile}>
          <div className={styles.statValue}>{stats ? stats.categories : <Skeleton width={40} height={34} />}</div>
          <div className={styles.statLabel}>فئة</div>
        </div>
      </div>

      <div className={styles.quick}>
        <Link to="/admin/products" className={styles.quickCard}>
          <b>إدارة المنتجات</b><span>إضافة، تعديل، أو تعطيل المنتجات ورفع صورها.</span>
        </Link>
        <Link to="/admin/categories" className={styles.quickCard}>
          <b>إدارة الفئات</b><span>تنظيم فئات المتجر.</span>
        </Link>
        <Link to="/admin/orders" className={styles.quickCard}>
          <b>الطلبات</b><span>متابعة الطلبات وتحديث حالتها.</span>
        </Link>
      </div>
    </div>
  );
}
