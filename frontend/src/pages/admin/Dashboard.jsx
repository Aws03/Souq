import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import styles from './Admin.module.css';

// لوحة تحكّم الأدمن: ترحيب + إحصاءات حقيقية من الـ API (لا أرقام وهمية).
export default function Dashboard() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const [stats, setStats] = useState(null);
  const [lowStockCount, setLowStockCount] = useState(0);
  const [error, setError] = useState(null);

  useEffect(() => {
    Promise.all([api.getProducts({ pageSize: 1 }), api.getCategories()])
      .then(([products, categories]) => setStats({ products: products.totalCount, categories: categories.length }))
      .catch((e) => setError(e.message));
    // تنبيه المخزون المنخفض مستقلّ عن الإحصاءات (فشله لا يمنع عرضها) — يكفيه العدد
    // (pageSize=1 ثم totalCount) لا تحميل كل المنتجات المنخفضة.
    api.getLowStock({ pageSize: 1 }).then((res) => setLowStockCount(res.totalCount)).catch(() => setLowStockCount(0));
  }, []);

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.dashboard.greeting', { name: user?.fullName || t('admin.adminFallback') })}</h2>
      <p className={styles.pageSub}>{t('admin.dashboard.subtitle')}</p>

      {error && <ErrorBanner message={error} />}

      {lowStockCount > 0 && (
        <Link to="/admin/inventory" className={styles.alertCard}>
          <b>{t('admin.inventory.lowStockBadge', { count: lowStockCount })}</b>
          <span>{t('admin.inventory.lowStockBadgeHint')}</span>
        </Link>
      )}

      <div className={styles.statRow}>
        <div className={styles.statTile}>
          <div className={styles.statValue}>{stats ? stats.products : <Skeleton width={60} height={34} />}</div>
          <div className={styles.statLabel}>{t('admin.dashboard.productsLabel')}</div>
        </div>
        <div className={styles.statTile}>
          <div className={styles.statValue}>{stats ? stats.categories : <Skeleton width={40} height={34} />}</div>
          <div className={styles.statLabel}>{t('admin.dashboard.categoriesLabel')}</div>
        </div>
      </div>

      <div className={styles.quick}>
        <Link to="/admin/products" className={styles.quickCard}>
          <b>{t('admin.dashboard.manageProducts')}</b><span>{t('admin.dashboard.manageProductsDesc')}</span>
        </Link>
        <Link to="/admin/categories" className={styles.quickCard}>
          <b>{t('admin.dashboard.manageCategories')}</b><span>{t('admin.dashboard.manageCategoriesDesc')}</span>
        </Link>
        <Link to="/admin/orders" className={styles.quickCard}>
          <b>{t('admin.dashboard.ordersTitle')}</b><span>{t('admin.dashboard.ordersDesc')}</span>
        </Link>
      </div>
    </div>
  );
}
