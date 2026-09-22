import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { contrastRatio, pickText, themeVariables } from '../../app/tenantModel';
import { loadPreviewFonts } from '../../app/storeTheme';
import { CartIcon } from '../../components/icons/Icons';
import styles from './StorePreview.module.css';

// ============================================================================
// معاينة الهوية قبل حفظها. الرموز نفسها التي يكتبها applyStoreTheme على <html> تُكتب هنا على
// إطار المعاينة وحده — فما يراه التاجر هو الاشتقاق الفعلي (الوضع الداكن، نصّ الأزرار، اللوحة المقلوبة)
// لا تقريباً له، ولا يتلوّن باقي اللوحة بتجربة لم تُحفظ.
//
// لا منتجات ولا أسعار مخترعة: بطاقة المنتج هيكل (مستطيلات)، والنصوص نصوص المتجر نفسه (اسمه، شريط
// إعلانه، وصفه) أو عناوين الواجهة الحقيقية. معاينة تعرض منتجاً باسم وسعر مخترعَين تضع في متجر التاجر
// شيئاً لم يضعه هو.
// ============================================================================
export default function StorePreview({ branding, texts, logoUrl, fallbackName }) {
  const { t, i18n } = useTranslation();
  const [mode, setMode] = useState('light');
  const language = i18n.language;
  // القالبُ مُدخَلٌ هنا كما هو في `applyStoreTheme` (C8): المعاينةُ هي الموضعُ الذي يجرّب فيه
  // التاجرُ الثلاثةَ قبل أن يحفظ، ومعاينةٌ لا تُظهر الفرق تجعله يحفظ ليكتشفه — أو لا يحفظ أصلاً.
  const preset = branding.themePreset || 'classic';
  const variables = useMemo(() => themeVariables(branding, mode, preset), [branding, mode, preset]);

  useEffect(() => { if (branding.typography) loadPreviewFonts(branding.typography); }, [branding.typography]);

  const name = pickText(texts.displayName, language, texts.defaultCulture) || fallbackName;
  const announcement = pickText(texts.announcement, language, texts.defaultCulture);
  const description = pickText(texts.seoDescription, language, texts.defaultCulture);
  const ratio = contrastRatio(variables['--color-text'], variables['--color-bg']);

  return (
    <figure className={styles.wrap} aria-labelledby="store-preview-caption">
      <div className={styles.toolbar}>
        <figcaption id="store-preview-caption" className={styles.caption}>{t('admin.settings.preview.title')}</figcaption>
        <div className={styles.modes} role="group" aria-label={t('admin.settings.preview.modeLabel')}>
          {['light', 'dark'].map((value) => (
            <button key={value} type="button" aria-pressed={mode === value}
              className={`${styles.mode} ${mode === value ? styles.modeActive : ''}`} onClick={() => setMode(value)}>
              {t(`admin.settings.themeMode.${value}`)}
            </button>
          ))}
        </div>
      </div>

      {/* الإطار زخرفيّ للقارئ الآلي: نصوصه هي نفسها الحقول أعلاه، وقراءتها مرّتين ضجيج. */}
      <div className={styles.frame} style={variables} data-theme={mode} data-preset={preset} aria-hidden="true">
        {announcement && <div className={styles.announcement}>{announcement}</div>}
        <div className={styles.header}>
          {logoUrl ? <img className={styles.logo} src={logoUrl} alt="" /> : <span className={styles.name}>{name}</span>}
          <CartIcon size={18} />
        </div>
        <div className={styles.hero}>
          <strong className={styles.heroTitle}>{name}</strong>
          {description && <p className={styles.heroText}>{description}</p>}
          <span className={styles.primaryButton}>{t('admin.settings.preview.primaryAction')}</span>
        </div>
        <div className={styles.body}>
          <div className={styles.card}>
            <div className={styles.media} />
            <div className={styles.line} />
            <div className={`${styles.line} ${styles.short}`} />
            <span className={styles.accentButton}>{t('admin.settings.preview.accentAction')}</span>
          </div>
          <div className={styles.copy}>
            <p className={styles.text}>{t('admin.settings.preview.bodyText')}</p>
            <p className={styles.muted}>{t('admin.settings.preview.mutedText')}</p>
          </div>
        </div>
      </div>

      <p className={styles.note}>
        {t('admin.settings.preview.contrastNote', { ratio: ratio.toFixed(1) })}
        {' '}{mode === 'dark' && t('admin.settings.preview.darkNote')}
      </p>
    </figure>
  );
}
