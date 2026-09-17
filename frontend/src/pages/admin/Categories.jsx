import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Button from '../../components/common/Button';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import { getCategoryName } from '../../components/product/ProductBadges';
import { activationPayload, orderAsTree } from '../../features/admin/categories/categoryForm';
import CategoryFormDrawer from './CategoryFormDrawer';
import styles from './Admin.module.css';

// شاشة إدارة الفئات (المرحلة 5): كل الفئات من /admin/categories — بما فيها المخفيّة — مرتّبة شجرياً بإزاحة للفروع.
// الإخفاء يُبعد الفئة ومنتجاتها عن المتجر دون حذف؛ الحذف لفئة فارغة بلا فروع فقط (يحرسه الخادم).
export default function Categories() {
  const { t } = useTranslation();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api.getAdminCategories()
      .then((res) => { setCategories(res); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { load(); }, [load]);

  const rows = useMemo(() => orderAsTree(categories), [categories]);
  const parentName = (id) => {
    const parent = categories.find((c) => c.id === id);
    return parent ? getCategoryName(parent) : '—';
  };

  const save = async (payload) => {
    if (editing?.id) await api.updateCategory(editing.id, payload);
    else await api.createCategory(payload);
    setEditing(null);
    toast.success(editing?.id ? t('admin.categories.updated') : t('admin.categories.created'));
    load();
  };

  const toggleActive = async (category) => {
    try {
      await api.updateCategory(category.id, activationPayload(category, !category.isActive));
      toast.success(t(category.isActive ? 'admin.categories.deactivated' : 'admin.categories.activated'));
      load();
    } catch (e) { toast.error(e.message); }
  };

  const remove = (category) => confirmation.ask({
    title: t('admin.categories.confirmDelete.title', { name: getCategoryName(category) }),
    message: t('admin.categories.confirmDelete.message'),
    confirmLabel: t('admin.categories.confirmDelete.action'),
    danger: true,
    action: async () => {
      await api.deleteCategory(category.id);
      toast.success(t('admin.categories.deleted'));
      load();
    },
  });

  const columns = [
    {
      key: 'name', header: t('admin.categories.colName'), truncate: true, tooltip: (c) => getCategoryName(c),
      render: (c) => <span style={{ paddingInlineStart: `${c.depth * 18}px` }}>{getCategoryName(c)}</span>,
    },
    { key: 'slug', header: t('admin.categories.colSlug'), width: '150px', render: (c) => <span dir="ltr">{c.slug}</span> },
    { key: 'parent', header: t('admin.categories.colParent'), width: '150px', truncate: true, render: (c) => (c.parentId ? parentName(c.parentId) : '—') },
    { key: 'order', header: t('admin.categories.colSortOrder'), width: '80px', align: 'end', render: (c) => c.sortOrder },
    {
      key: 'status', header: t('admin.categories.colStatus'), width: '100px',
      render: (c) => (
        <span className={`${styles.statusBadge} ${c.isActive ? styles.delivered : styles.cancelled}`}>
          {c.isActive ? t('admin.categories.active') : t('admin.categories.inactive')}
        </span>
      ),
    },
    {
      key: 'actions', header: t('admin.categories.colActions'), width: '64px', align: 'end', render: (c) => (
        <RowActionsMenu actions={[
          { label: t('common.edit'), onClick: () => setEditing(c) },
          { label: c.isActive ? t('admin.categories.deactivate') : t('admin.categories.activate'), onClick: () => toggleActive(c) },
          { label: t('common.delete'), variant: 'danger', onClick: () => remove(c) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.categories.title')}</h2>
      <p className={styles.pageSub}>{t('admin.categories.subtitle')}</p>

      <div className={styles.toolbar}>
        <Button variant="primary" onClick={() => setEditing({})}>{t('admin.categories.addCategory')}</Button>
      </div>

      <DataTable columns={columns} rows={rows} rowKey={(c) => c.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.categories.emptyTitle')} emptyMessage={t('admin.categories.emptyMessage')}
        minWidth="640px" stickyFirstColumn />

      {editing !== null && (
        <CategoryFormDrawer category={editing.id ? editing : null} categories={rows}
          onSave={save} onClose={() => setEditing(null)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
