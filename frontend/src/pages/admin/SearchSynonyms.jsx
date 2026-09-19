import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Button from '../../components/common/Button';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import Tabs, { TabPanel } from '../../components/common/Tabs';
import SearchInsightsPanel from './SearchInsightsPanel';
import SearchSynonymFormDrawer from './SearchSynonymFormDrawer';
import styles from './Admin.module.css';

// ============================================================================
// بحث المتجر — لسانان (M3 + M13، صلاحية catalog.manage).
//
// **المفردات** (M3، ADR-0042): التاجر يُعلِّم محرّك البحث كلمات زبائنه التي لا ترد في كتالوجه — لهجة محلية
// أو اسم تجاري شائع. ما لا يبلغه التصحيح الآلي، لأنّه يعمل على مسافة تحرير من كلمات الكتالوج نفسه فلا يصل
// إلى كلمة لا تشبه أياً منها. وتُعرض **الصورة المطبَّعة** إلى جانب ما كتبه التاجر: هي ما يُطابَق فعلاً،
// وإظهارها يفسّر لماذا رُفض زوج يبدو جديداً ("مكنسة" و"مكنسه" الكلمة نفسها هنا) بدل أن يبدو تعسّفاً.
//
// **وأثر البحث** (M13): ما بحث عنه الزبائن فعلاً، وأيّ كلمةٍ لم تجد شيئاً.
//
// **ولمَ لسانان في شاشة لا شاشتان في القائمة؟** لأنّ الاثنين عملٌ واحد: الأثر يقول أيّ كلمةٍ تحتاج مرادفاً،
// والمفردات هي حيث يُكتب. فصلُهما كان سيُضيف بنداً رابع عشر إلى قائمةٍ طويلة أصلاً، ويجعل الطريق بين
// السؤال وجوابه تنقّلاً بين شاشتين ونسخَ نصٍّ بينهما. واللسان الأول هو الأثر: من يفتح هذه الشاشة يفتحها
// ليعرف ما يفعل، ثم يفعله.
//
// وزرّ "أضِفها مرادفاً" في صفّ الأثر يفتح درج المفردات **مملوءاً بالكلمة ولغتها** — فلا يبقى على التاجر
// إلا الكلمة التي يريد البحث بها.
// ============================================================================
export default function SearchSynonyms() {
  const { t } = useTranslation();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(null);
  const [tab, setTab] = useState('insights');
  const tabsBase = useId();

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

  // من الأثر إلى المفردات: الكلمة ولغتها تُحمَلان إلى الدرج، ويُنتقل إلى لسان المفردات كي يرى التاجر
  // نتيجة إضافته في سياقها — لا يبقى في شاشةٍ لا تُظهر ما فعله.
  const addSynonymFor = (insight) => {
    setEditing({ culture: insight.culture, term: insight.term, expansion: '' });
    setTab('vocabulary');
  };

  const tabs = [
    { id: 'insights', label: t('admin.searchInsights.tab') },
    { id: 'vocabulary', label: t('admin.searchSynonyms.tab'), badge: items.length || undefined },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.storeSearch.title')}</h2>
      <p className={styles.pageSub}>{t('admin.storeSearch.subtitle')}</p>

      <Tabs tabs={tabs} active={tab} onChange={setTab} base={tabsBase} label={t('admin.storeSearch.tabsLabel')} />

      {tab === 'insights' && (
        <TabPanel id="insights" base={tabsBase}>
          <SearchInsightsPanel onAddSynonym={addSynonymFor} />
        </TabPanel>
      )}

      {tab === 'vocabulary' && (
        <TabPanel id="vocabulary" base={tabsBase}>
          <p className={styles.pageSub}>{t('admin.searchSynonyms.subtitle')}</p>

          <div className={styles.toolbar}>
            <Button variant="primary" onClick={() => setEditing({})}>{t('admin.searchSynonyms.add')}</Button>
          </div>

          <DataTable label={t('admin.searchSynonyms.title')} columns={columns} rows={items} rowKey={(s) => s.id} loading={isPending} error={error?.message}
            onRetry={refetch} emptyTitle={t('admin.searchSynonyms.emptyTitle')}
            emptyMessage={t('admin.searchSynonyms.emptyMessage')} minWidth="760px" stickyFirstColumn />
        </TabPanel>
      )}

      {editing !== null && (
        <SearchSynonymFormDrawer synonym={editing.id ? editing : null} prefill={editing.id ? null : editing}
          onSave={save} onClose={() => setEditing(null)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
