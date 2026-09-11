import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { getCategoryName } from '../../components/product/ProductBadges';
import { hasAnyName, textsToForm } from '../../features/catalog/catalogText';
import { buildCategoryPayload, descendantIds } from '../../features/admin/categories/categoryForm';
import styles from './CategoryFormDrawer.module.css';

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
// إزاحة الفروع داخل <option>: المسافات العادية تُطوى، فنستخدم مسافات غير منكسرة.
const indent = (depth) => '   '.repeat(depth ?? 0);

// درج إضافة/تعديل فئة (المرحلة 5): الاسم لكل لغة، المعرّف، الأب، ترتيب العرض، والظهور في المتجر. خيارات الأب
// تستبعد الفئة نفسها وفروعها (حلقة)؛ الخادم يحرس ذلك وحدّ العمق (5 مستويات) برسالة واضحة على أي حال.
// categories تصل مرتّبة شجرياً بعمق كل فئة، فتُزاح الخيارات كما في الجدول.
export default function CategoryFormDrawer({ category, categories, onSave, onClose }) {
  const { t } = useTranslation();
  const isEdit = !!category;
  const [texts, setTexts] = useState(() => textsToForm(category?.translations));
  const [slug, setSlug] = useState(category?.slug ?? '');
  const [parentId, setParentId] = useState(category?.parentId ?? '');
  const [sortOrder, setSortOrder] = useState(category?.sortOrder ?? 0);
  const [isActive, setIsActive] = useState(category?.isActive ?? true);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const parentOptions = useMemo(() => {
    if (!category) return categories;
    const excluded = descendantIds(categories, category.id).add(category.id);
    return categories.filter((c) => !excluded.has(c.id));
  }, [categories, category]);

  const setName = (culture) => (e) =>
    setTexts((current) => ({ ...current, [culture]: { ...current[culture], name: e.target.value } }));

  const submit = async (e) => {
    e.preventDefault();
    if (!hasAnyName(texts)) return setError(t('admin.categoryForm.nameRequired'));
    if (!SLUG_PATTERN.test(slug.trim())) return setError(t('admin.categoryForm.slugInvalid'));

    setBusy(true); setError(null);
    try {
      await onSave(buildCategoryPayload({ texts, slug, parentId, sortOrder, isActive }));
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
          <input className={inputClass(false)} value={texts.ar.name} dir="rtl" maxLength={200}
            onChange={setName('ar')} placeholder={t('admin.categoryForm.namePlaceholder')} />
        </FormField>
        <FormField label={t('admin.categoryForm.nameEnLabel')} hint={t('admin.categoryForm.nameEnHint')}>
          <input className={inputClass(false)} value={texts.en.name} dir="ltr" maxLength={200}
            onChange={setName('en')} placeholder="e.g. Electronics" />
        </FormField>

        <FormField label={t('admin.categoryForm.slugLabel')}>
          <input className={inputClass(false)} value={slug} maxLength={100} dir="ltr" placeholder="electronics"
            onChange={(e) => setSlug(e.target.value.toLowerCase())} />
        </FormField>

        <FormField label={t('admin.categoryForm.parentLabel')}>
          <select className={inputClass(false)} value={parentId} onChange={(e) => setParentId(e.target.value)}>
            <option value="">{t('admin.categoryForm.noParent')}</option>
            {parentOptions.map((c) => <option key={c.id} value={c.id}>{indent(c.depth)}{getCategoryName(c)}</option>)}
          </select>
        </FormField>

        <FormField label={t('admin.categoryForm.sortOrderLabel')} hint={t('admin.categoryForm.sortOrderHint')}>
          <input className={inputClass(false)} type="number" min="0" step="1" value={sortOrder}
            onChange={(e) => setSortOrder(e.target.value)} />
        </FormField>

        <label className={styles.checkboxRow}>
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          {t('admin.categoryForm.activeLabel')}
        </label>
      </form>
    </Drawer>
  );
}
