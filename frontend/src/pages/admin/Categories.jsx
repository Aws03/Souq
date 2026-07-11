import { useState, useEffect, useCallback } from 'react';
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
    toast.success(editing?.id ? 'تم تحديث الفئة' : 'تمت إضافة الفئة');
    load();
  };

  const remove = async (category) => {
    if (!window.confirm(`حذف الفئة "${category.name}"؟`)) return;
    try {
      await api.deleteCategory(category.id);
      toast.success('تم حذف الفئة');
      load();
    } catch (e) { toast.error(e.message); }
  };

  const columns = [
    { key: 'name', header: 'الاسم', render: (c) => c.name },
    { key: 'slug', header: 'المُعرّف', render: (c) => <span dir="ltr">{c.slug}</span> },
    { key: 'parent', header: 'الفئة الأب', render: (c) => (c.parentId ? parentName(c.parentId) : '—') },
    {
      key: 'actions', header: '', render: (c) => (
        <RowActionsMenu actions={[
          { label: 'تعديل', onClick: () => setEditing(c) },
          { label: 'حذف', variant: 'danger', onClick: () => remove(c) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>إدارة الفئات</h2>
      <p className={styles.pageSub}>نظّم فئات المتجر وفئاتها الفرعية.</p>

      <div className={styles.toolbar}>
        <Button variant="primary" onClick={() => setEditing({})}>+ إضافة فئة</Button>
      </div>

      <DataTable columns={columns} rows={categories} rowKey={(c) => c.id} loading={loading} error={error}
        onRetry={load} emptyTitle="لا فئات بعد" emptyMessage="أضف أول فئة لتنظيم منتجاتك." />

      {editing !== null && (
        <CategoryFormDrawer category={editing.id ? editing : null} categories={categories}
          onSave={save} onClose={() => setEditing(null)} />
      )}
    </div>
  );
}
