import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { buildSynonymPayload, synonymFormProblem, synonymToForm } from '../../features/admin/search/synonymForm';
import styles from './CategoryFormDrawer.module.css';

// درج إضافة/تعديل مفردة بحث (M3، ADR-0042): كلمة يكتبها المتسوّق ⇒ كلمة تُبحث معها. اتجاه الحقل يتبع اللغة
// المختارة لا لغة الواجهة: تاجر يعمل بواجهة عربية قد يُضيف زوجاً إنجليزياً.
export default function SearchSynonymFormDrawer({ synonym, onSave, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!synonym;
  const [form, setForm] = useState(() => synonymToForm(synonym));
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));
  const dir = form.culture === 'en' ? 'ltr' : 'rtl';

  const submit = async (e) => {
    e.preventDefault();
    const problem = synonymFormProblem(form);
    if (problem) return setError(t(`admin.searchSynonyms.form.${problem}`));

    setBusy(true); setError(null);
    try { await onSave(buildSynonymPayload(form)); }
    catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy}
      title={isEdit ? t('admin.searchSynonyms.form.editTitle') : t('admin.searchSynonyms.form.addTitle')}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="search-synonym-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="search-synonym-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}

        <FormField label={t('admin.searchSynonyms.form.cultureLabel')}>
          <select className={inputClass(false)} value={form.culture} onChange={set('culture')}>
            <option value="ar">{t('admin.searchSynonyms.form.cultureAr')}</option>
            <option value="en">{t('admin.searchSynonyms.form.cultureEn')}</option>
          </select>
        </FormField>

        <FormField label={t('admin.searchSynonyms.form.termLabel')} hint={t('admin.searchSynonyms.form.termHint')}>
          <input className={inputClass(false)} value={form.term} onChange={set('term')} dir={dir} maxLength={100} />
        </FormField>

        <FormField label={t('admin.searchSynonyms.form.expansionLabel')} hint={t('admin.searchSynonyms.form.expansionHint')}>
          <input className={inputClass(false)} value={form.expansion} onChange={set('expansion')} dir={dir} maxLength={100} />
        </FormField>
      </form>
    </Drawer>
  );
}
