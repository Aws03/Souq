import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import styles from './CategoryFormDrawer.module.css';

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

// درج إضافة/تعديل فئة. فئة لا يمكن أن تكون أباً لنفسها — الحلقات الأعمق يرفضها
// الخادم برسالة واضحة.
export default function CategoryFormDrawer({ category, categories, onSave, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!category;
  const [name, setName] = useState(category?.name ?? '');
  const [slug, setSlug] = useState(category?.slug ?? '');
  const [parentId, setParentId] = useState(category?.parentId ?? '');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const parentOptions = categories.filter((c) => c.id !== category?.id);

  const submit = async (e) => {
    e.preventDefault();
    if (!name.trim()) return setError(t('admin.categoryForm.nameRequired'));
    if (!SLUG_PATTERN.test(slug.trim())) return setError(t('admin.categoryForm.slugInvalid'));

    setBusy(true); setError(null);
    try {
      await onSave({ name: name.trim(), slug: slug.trim(), parentId: parentId ? Number(parentId) : null });
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={isEdit ? t('admin.categoryForm.editTitle') : t('admin.categoryForm.addTitle')}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="category-form" loading={busy}>{t('common.save')}</Button>
        </div>
      }>
      <form id="category-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}

        <FormField label={t('admin.categoryForm.nameLabel')}>
          <input className={inputClass(false)} value={name} onChange={(e) => setName(e.target.value)} placeholder={t('admin.categoryForm.namePlaceholder')} />
        </FormField>

        <FormField label={t('admin.categoryForm.slugLabel')}>
          <input className={inputClass(false)} value={slug} onChange={(e) => setSlug(e.target.value.toLowerCase())} placeholder="electronics" dir="ltr" />
        </FormField>

        <FormField label={t('admin.categoryForm.parentLabel')}>
          <select className={inputClass(false)} value={parentId} onChange={(e) => setParentId(e.target.value)}>
            <option value="">{t('admin.categoryForm.noParent')}</option>
            {parentOptions.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </FormField>
      </form>
    </Drawer>
  );
}
