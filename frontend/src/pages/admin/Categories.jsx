import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Button from '../../components/common/Button';
import CategoryFormDrawer from './CategoryFormDrawer';
import styles from './Admin.module.css';

// شاشة إدارة الفئات. القائمة صغيرة عادةً (غير مرقّمة في الـ API) فنعرضها كاملة
// كجدول مسطّح، مع اسم الفئة الأب محلولاً من القائمة نفسها.
export default function Categories() {
  const { t } = useTranslation();
  const toast = useToast();
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api.getCategories()
      .then((res) => { setCategories(res); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { load(); }, [load]);

  const parentName = (id) => categories.find((c) => c.id === id)?.name || '—';

  const save = async (payload) => {
    if (editing?.id) await api.updateCategory(editing.id, payload);
    else await api.createCategory(payload);
    setEditing(null);
    toast.success(editing?.id ? t('admin.categories.updated') : t('admin.categories.created'));
    load();
  };

  const remove = async (category) => {
    if (!window.confirm(t('admin.categories.confirmDelete', { name: category.name }))) return;
    try {
      await api.deleteCategory(category.id);
      toast.success(t('admin.categories.deleted'));
      load();
    } catch (e) { toast.error(e.message); }
  };

  const columns = [
    { key: 'name', header: t('admin.categories.colName'), truncate: true, tooltip: (c) => c.name, render: (c) => c.name },
    { key: 'slug', header: t('admin.categories.colSlug'), width: '160px', render: (c) => <span dir="ltr">{c.slug}</span> },
    { key: 'parent', header: t('admin.categories.colParent'), width: '160px', truncate: true, render: (c) => (c.parentId ? parentName(c.parentId) : '—') },
    {
      key: 'actions', header: t('admin.categories.colActions'), width: '64px', align: 'end', render: (c) => (
        <RowActionsMenu actions={[
          { label: t('common.edit'), onClick: () => setEditing(c) },
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

      <DataTable columns={columns} rows={categories} rowKey={(c) => c.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.categories.emptyTitle')} emptyMessage={t('admin.categories.emptyMessage')}
        minWidth="480px" stickyFirstColumn />

      {editing !== null && (
        <CategoryFormDrawer category={editing.id ? editing : null} categories={categories}
          onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
