import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Button from '../../components/common/Button';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import SearchSynonymFormDrawer from './SearchSynonymFormDrawer';
import styles from './Admin.module.css';

// ============================================================================
// مفردات بحث المتجر (M3، ADR-0042، صلاحية catalog.manage): التاجر يُعلِّم محرّك البحث كلمات زبائنه التي لا
// ترد في كتالوجه — لهجة محلية أو اسم تجاري شائع. ما لا يبلغه التصحيح الآلي، لأنّه يعمل على مسافة تحرير من
// كلمات الكتالوج نفسه فلا يصل إلى كلمة لا تشبه أياً منها.
//
// تُعرض **الصورة المطبَّعة** إلى جانب ما كتبه التاجر: هي ما يُطابَق فعلاً، وإظهارها يفسّر لماذا رُفض زوج
// يبدو جديداً ("مكنسة" و"مكنسه" الكلمة نفسها هنا) بدل أن يبدو الرفض تعسّفاً.
// ============================================================================
export default function SearchSynonyms() {
  const { t } = useTranslation();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(null);

  // قائمة كاملة بلا ترقيم (TD-25، M10): المكسب أنّها تُقرأ من الذاكرة المؤقّتة عند العودة
  // إليها، وأنّ الإنعاش بعد تعديلٍ صار إبطالَ مفتاحٍ لا نداءً ثانياً مكتوباً بيد.
  const { data: items = [], error, isPending, refetch } = useQuery({
    queryKey: queryKeys.adminSynonyms({}),
    queryFn: api.getSearchSynonyms,
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.adminSynonymsAll() });

  const save = async (payload) => {
    if (editing?.id) await api.updateSearchSynonym(editing.id, payload);
    else await api.createSearchSynonym(payload);
    toast.success(editing?.id ? t('admin.searchSynonyms.updated') : t('admin.searchSynonyms.created'));
    setEditing(null);
    reload();
  };

  const remove = (synonym) => confirmation.ask({
    title: t('admin.searchSynonyms.confirmDelete.title', { term: synonym.term }),
    message: t('admin.searchSynonyms.confirmDelete.message'),
    confirmLabel: t('admin.searchSynonyms.confirmDelete.action'),
    danger: true,
    action: async () => {
      await api.deleteSearchSynonym(synonym.id);
      toast.success(t('admin.searchSynonyms.deleted'));
      reload();
    },
  });

  const columns = [
    {
      key: 'culture', header: t('admin.searchSynonyms.colCulture'), width: '90px',
      render: (s) => t(`admin.searchSynonyms.form.culture${s.culture === 'en' ? 'En' : 'Ar'}`),
    },
    {
      key: 'term', header: t('admin.searchSynonyms.colTerm'), width: '180px', truncate: true,
      tooltip: (s) => s.term, render: (s) => s.term,
    },
    {
      key: 'expansion', header: t('admin.searchSynonyms.colExpansion'), width: '180px', truncate: true,
      tooltip: (s) => s.expansion, render: (s) => s.expansion,
    },
    {
      key: 'normalized', header: t('admin.searchSynonyms.colMatched'), width: '220px', truncate: true,
      tooltip: (s) => `${s.termNormalized} → ${s.expansionNormalized}`,
      render: (s) => <span className={styles.muted}>{s.termNormalized} → {s.expansionNormalized}</span>,
    },
    {
      key: 'actions', header: t('admin.coupons.colActions'), width: '64px', align: 'end', render: (s) => (
        <RowActionsMenu actions={[
          { label: t('common.edit'), onClick: () => setEditing(s) },
          { label: t('common.delete'), variant: 'danger', onClick: () => remove(s) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.searchSynonyms.title')}</h2>
      <p className={styles.pageSub}>{t('admin.searchSynonyms.subtitle')}</p>

      <div className={styles.toolbar}>
        <Button variant="primary" onClick={() => setEditing({})}>{t('admin.searchSynonyms.add')}</Button>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(s) => s.id} loading={isPending} error={error?.message}
        onRetry={refetch} emptyTitle={t('admin.searchSynonyms.emptyTitle')}
        emptyMessage={t('admin.searchSynonyms.emptyMessage')} minWidth="760px" stickyFirstColumn />

      {editing !== null && (
        <SearchSynonymFormDrawer synonym={editing.id ? editing : null} onSave={save} onClose={() => setEditing(null)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
