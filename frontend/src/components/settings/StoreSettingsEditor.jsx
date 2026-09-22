import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useToast } from '../../context/ToastContext';
import { inputClass } from '../common/FormField';
import Button from '../common/Button';
import Skeleton from '../common/Skeleton';
import { ErrorBanner } from '../common/StateViews';
import { TrashIcon } from '../icons/Icons';
import {
  COLOR_FIELDS, buildSettingsPayload, colorChecks, hasChanges, moveSection, previewBranding, problemFor,
  settingsProblems, toggleSection,
  settingsToForm,
} from '../../features/admin/settings/settingsForm';
import BrandingAssetField from './BrandingAssetField';
import StorePreview from './StorePreview';
import styles from './StoreSettingsEditor.module.css';

// ============================================================================
// محرّر إعدادات متجر واحد — مشترك بين مسارَي التعديل اللذين يقبلهما الخادم بالعقد نفسه (StoreSettingsInput):
//   • مدير المتجر بعد التسليم (/admin/settings، المتجر من المضيف)،
//   • والمنصّة عند التجهيز وبعده (/platform/stores/:id، المتجر من المسار).
// الفرق بينهما نقل لا قواعد: من أين يُقرأ وإلى أين يُكتب ويُرفع. ذلك يأتي في `source`، وكل ما سواه — الفحص المسبق،
// المسودّة، المعاينة، الحدود من الخادم — واحد. نسختان من هذا النموذج كانتا ستفترقان في أوّل قاعدة تُضاف.
//
// ما يُعدَّل هنا ما يملكه المتجر بعد التسليم (WhiteLabel.md §2): اسمه وهويّته ولغاته وتواصله وSEO وشريط إعلانه.
// العملة والنطاقات والوحدات ليست هنا في أيّ من المسارين.
//
// ثلاثة قرارات:
//   • القوائم والحدود من الخادم (options): خطٌّ يُضاف هناك يظهر هنا بلا تعديل.
//   • PUT يستبدل الإعدادات كاملةً، فالنموذج يُبنى من قراءة كاملة ويُرسل كاملاً — لا حقول تُنسى.
//   • الملفات تُرفع فوراً ولا تنتظر "حفظ"، ولا تمسّ تعديلاً لم يُحفظ في باقي النموذج.
//
// source: {
//   settingsKey, loadSettings(),          // مفتاح الاستعلام وقراءة StoreSettingsDto
//   optionsKey, loadOptions(),            // StoreSettingsOptionsDto
//   save(payload), upload(asset, file),   // asset: logo | favicon | social-image ⇒ { url }
//   onSaved?(), fallbackName, currencyHint // ما بعد الحفظ (تحديث هوية اللوحة)، الاسم الاحتياطي للمعاينة، تلميح العملة
// }
// actions: عناصر إضافية في شريط الحفظ (المعالج: "رجوع" و"متابعة").
// ============================================================================
const fieldId = (field) => `settings-${field.replace(/\./g, '-')}`;

export default function StoreSettingsEditor({ source, actions = null, onDirtyChange }) {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();

  const settingsQuery = useQuery({ queryKey: source.settingsKey, queryFn: source.loadSettings });
  const optionsQuery = useQuery({ queryKey: source.optionsKey, queryFn: source.loadOptions, staleTime: Infinity });
  const settings = settingsQuery.data;
  const options = optionsQuery.data;

  const saved = useMemo(() => (settings && options ? settingsToForm(settings, options) : null), [settings, options]);
  // المسودّة null حتى يلمس المدير شيئاً: النموذج هو المحفوظ نفسه، فتصل إليه أيّ قراءة أحدث (رفع شعار،
  // عودة إلى النافذة). أوّل تعديل ينسخه مسودّةً، ومن تلك اللحظة لا تمسح قراءةٌ جديدة ما كُتب.
  const [draft, setDraft] = useState(null);
  const form = draft ?? saved;
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [serverError, setServerError] = useState(null);

  const dirty = !!(form && saved && hasChanges(form, saved));
  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);

  // مغادرة الصفحة (إغلاق التبويب، إعادة التحميل) مع تعديلات غير محفوظة تسأل أوّلاً.
  useEffect(() => {
    if (!dirty) return undefined;
    const warn = (event) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  const error = settingsQuery.error ?? optionsQuery.error;
  if (error) {
    return (
      <div>
        <ErrorBanner message={error.message} onRetry={() => { settingsQuery.refetch(); optionsQuery.refetch(); }} />
      </div>
    );
  }
  if (!form || !options) {
    return (
      <div aria-busy="true">
        <Skeleton height={220} radius={14} />
      </div>
    );
  }

  const problems = settingsProblems(form, options);
  const visibleProblems = submitted ? problems : [];
  const fieldError = (field) => {
    const problem = problemFor(visibleProblems, field);
    return problem ? t(`admin.settings.problem.${problem.key}`, problem.values) : null;
  };

  const edit = (change) => setDraft((current) => change(current ?? saved));
  const update = (patch) => edit((current) => ({ ...current, ...patch }));
  const updateText = (field, culture) => (event) => {
    const { value } = event.target;
    edit((current) => ({ ...current, [field]: { ...current[field], [culture]: value } }));
  };
  const updateColor = (field, value) =>
    edit((current) => ({ ...current, colors: { ...current.colors, [field]: value.toUpperCase() } }));

  const save = async (event) => {
    event.preventDefault();
    setSubmitted(true);
    setServerError(null);
    if (problems.length > 0) {
      // التركيز بلا تمرير المتصفّح، ثم تمرير إلى الوسط: التمرير الافتراضي يضع الحقل على حافّة الشاشة،
      // وهناك يغطّيه شريط الحفظ الملتصق — فيُركَّز حقلٌ لا يُرى.
      const first = document.getElementById(fieldId(problems[0].field));
      first?.focus({ preventScroll: true });
      first?.scrollIntoView?.({ block: 'center' });
      return;
    }
    setBusy(true);
    try {
      await source.save(buildSettingsPayload(form));
    } catch (err) {
      setServerError(err.message);
      setBusy(false);
      return;
    }
    setSubmitted(false);
    toast.success(t('admin.settings.saved'));
    source.onSaved?.();
    // النموذج يُعاد من قراءة الخادم لا ممّا أُرسل: الخادم يطبّع (حروف كبيرة، روابط، لغة افتراضية
    // تُضاف للمفعّلة). فشل هذه القراءة لا يعني أن الحفظ فشل — فلا يُعرض خطأً، والقراءة تُعاد لاحقاً.
    try {
      await queryClient.fetchQuery({ queryKey: source.settingsKey, queryFn: source.loadSettings, staleTime: 0 });
      setDraft(null);
    } catch {
      queryClient.invalidateQueries({ queryKey: source.settingsKey });
    } finally {
      setBusy(false);
    }
  };

  const upload = async (asset, file) => {
    const { url } = await source.upload(asset, file);
    const key = { logo: 'logoUrl', favicon: 'faviconUrl', 'social-image': 'socialImageUrl' }[asset];
    // الرابط وحده يُحدَّث في الذاكرة: إعادة جلب الإعدادات كانت ستعيد بناء قيم النموذج المحفوظة، لا الحالية.
    queryClient.setQueryData(source.settingsKey, (current) =>
      (current ? { ...current, branding: { ...current.branding, [key]: url } } : current));
    toast.success(t('admin.settings.assets.uploaded'));
    source.onSaved?.();
  };

  const cultureLabel = (culture) => t(`admin.settings.culture.${culture}`);
  const checks = colorChecks(form.colors, options.contrast);
  const policyKinds = options.policyKinds ?? [];
  const usedNetworks = new Set(form.social.map((link) => link.network));
  const nextNetwork = options.socialNetworks.find((n) => !usedNetworks.has(n.network))?.network;
  const locale = settings.locale;

  const localizedFields = (field, { multiline = false, max } = {}) => options.cultures.map((culture) => {
    const id = fieldId(`${field}.${culture}`);
    const value = form[field][culture] ?? '';
    const problem = fieldError(`${field}.${culture}`);
    const Tag = multiline ? 'textarea' : 'input';
    return (
      <div key={culture} className={styles.field}>
        <label className={styles.label} htmlFor={id}>
          {cultureLabel(culture)}
          {!form.enabledCultures.includes(culture) && (
            <span className={styles.labelNote}> · {t('admin.settings.cultureDisabled')}</span>
          )}
        </label>
        <Tag id={id} className={inputClass(!!problem, multiline ? styles.textarea : '')} value={value}
          lang={culture} dir={culture === 'ar' ? 'rtl' : 'ltr'} onChange={updateText(field, culture)}
          aria-invalid={!!problem} aria-describedby={`${id}-count`} rows={multiline ? 3 : undefined} />
        <span id={`${id}-count`} className={problem ? styles.fieldError : styles.hint}>
          {problem ?? t('admin.settings.counter', { length: value.trim().length, max })}
        </span>
      </div>
    );
  });

  return (
    <div className={styles.page}>

      <form id="store-settings-form" noValidate onSubmit={save} className={styles.form}>
        {serverError && <ErrorBanner message={serverError} />}
        {visibleProblems.length > 0 && (
          <ErrorBanner message={t('admin.settings.problemsSummary', { count: visibleProblems.length })} />
        )}

        {/* ── الهوية ── */}
        <section className={styles.section} aria-labelledby="section-identity">
          <h3 id="section-identity" className={styles.sectionTitle}>{t('admin.settings.section.identity')}</h3>
          <p className={styles.sectionHint}>{t('admin.settings.section.identityHint')}</p>
          <div className={styles.grid2}>{localizedFields('displayName', { max: options.limits.displayName })}</div>
          <div className={styles.assets}>
            {['logo', 'favicon', 'social-image'].map((asset) => (
              <BrandingAssetField key={asset} asset={asset} maxBytes={options.limits.brandingFileBytes} onUpload={upload}
                url={settings.branding[{ logo: 'logoUrl', favicon: 'faviconUrl', 'social-image': 'socialImageUrl' }[asset]]} />
            ))}
          </div>
        </section>

        {/* ── المظهر ── */}
        <section className={styles.section} aria-labelledby="section-appearance">
          <h3 id="section-appearance" className={styles.sectionTitle}>{t('admin.settings.section.appearance')}</h3>
          <p className={styles.sectionHint}>{t('admin.settings.section.appearanceHint')}</p>
          <div className={styles.appearance}>
            <div className={styles.appearanceControls}>
              <fieldset className={styles.fieldset}>
                <legend className={styles.label}>{t('admin.settings.colors.legend')}</legend>
                <div className={styles.colors}>
                  {COLOR_FIELDS.map((field) => {
                    const id = fieldId(`colors.${field}`);
                    const problem = fieldError(`colors.${field}`);
                    const valid = /^#[0-9a-f]{6}$/i.test(form.colors[field]);
                    return (
                      <div key={field} className={styles.color}>
                        <label className={styles.colorLabel} htmlFor={id}>{t(`admin.settings.colors.${field}`)}</label>
                        <div className={styles.colorRow}>
                          <input type="color" className={styles.swatch} value={valid ? form.colors[field].toLowerCase() : '#000000'}
                            aria-label={t('admin.settings.colors.pick', { color: t(`admin.settings.colors.${field}`) })}
                            onChange={(e) => updateColor(field, e.target.value)} />
                          <input id={id} className={inputClass(!!problem, styles.hex)} dir="ltr" spellCheck={false}
                            maxLength={7} value={form.colors[field]} aria-invalid={!!problem}
                            onChange={(e) => updateColor(field, e.target.value.trim())} />
                        </div>
                        {problem && <span className={styles.fieldError}>{problem}</span>}
                      </div>
                    );
                  })}
                </div>
              </fieldset>

              {checks.length > 0 && (
                <ul className={styles.checks} aria-label={t('admin.settings.contrast.title')}>
                  {checks.map((check) => (
                    <li key={check.id} className={check.pass ? styles.checkPass : styles.checkFail}>
                      <span aria-hidden="true">{check.pass ? '✓' : '✕'}</span>
                      <span>{t(`admin.settings.contrast.${check.id}`)}</span>
                      <span className={styles.ratio}>
                        {t('admin.settings.contrast.ratio', { ratio: check.ratio.toFixed(2), minimum: check.minimum })}
                      </span>
                      <span className={styles.srOnly}>
                        {check.pass ? t('admin.settings.contrast.pass') : t('admin.settings.contrast.fail')}
                      </span>
                    </li>
                  ))}
                </ul>
              )}

              <div className={styles.grid2}>
                <div className={styles.field}>
                  <label className={styles.label} htmlFor={fieldId('typography')}>{t('admin.settings.typography')}</label>
                  <select id={fieldId('typography')} className={inputClass(false)} value={form.typography}
                    onChange={(e) => update({ typography: e.target.value })}>
                    {options.typography.map((key) => (
                      <option key={key} value={key}>{t(`admin.settings.typographyOption.${key}`)}</option>
                    ))}
                  </select>
                </div>
                <div className={styles.field}>
                  <label className={styles.label} htmlFor={fieldId('themePreset')}>{t('admin.settings.themePreset')}</label>
                  <select id={fieldId('themePreset')} className={inputClass(false)} value={form.themePreset}
                    onChange={(e) => update({ themePreset: e.target.value })}>
                    {options.themePresets.map((key) => (
                      <option key={key} value={key}>{t(`admin.settings.themePresetOption.${key}`)}</option>
                    ))}
                  </select>
                  <span className={styles.hint}>{t('admin.settings.themePresetHint')}</span>
                </div>
              </div>

              <fieldset className={styles.fieldset}>
                <legend className={styles.label}>{t('admin.settings.themeModeLegend')}</legend>
                <div className={styles.choices}>
                  {options.themeModes.map((mode) => (
                    <label key={mode} className={styles.choice}>
                      <input type="radio" name="themeMode" value={mode} checked={form.themeMode === mode}
                        onChange={() => update({ themeMode: mode })} />
                      {t(`admin.settings.themeMode.${mode}`)}
                    </label>
                  ))}
                </div>
                <span className={styles.hint}>{t('admin.settings.themeModeHint')}</span>
              </fieldset>

              {/* ==========================================================
                  ترتيبُ أقسام الرئيسية (C8، ADR-0060). زرّان بخطوةٍ واحدة لا سحبٌ وإفلات:
                  السحبُ يحتاج بديلاً بلوحة المفاتيح وقارئَ شاشةٍ يفهمه، وزرٌّ بعنوانٍ صريح
                  يعمل لكلّ مُدخَلٍ بلا ذلك. والقائمةُ عموديّة، فلا «يمين/يسار» يقلبه اتجاهُ
                  الصفحة — والترتيبُ يُقرأ «الأوّل فالثاني» في العربية والإنجليزية معاً.
                  ========================================================== */}
              <fieldset className={styles.fieldset}>
                <legend className={styles.label}>{t('admin.settings.sections')}</legend>
                <span className={styles.hint}>{t('admin.settings.sectionsHint')}</span>
                <ol className={styles.sectionList}>
                  {form.sections.map((section, index) => {
                    const required = (options.requiredSections ?? []).includes(section.type);
                    const name = t(`admin.settings.sectionOption.${section.type}`);
                    return (
                      <li key={section.type} className={styles.sectionRow}>
                        <span className={styles.sectionOrder} aria-hidden="true">{index + 1}</span>
                        <label className={styles.sectionName}>
                          <input type="checkbox" checked={section.enabled} disabled={required}
                            onChange={() => update({ sections: toggleSection(form.sections, index) })} />
                          {name}
                          {required && <span className={styles.hint}> {t('admin.settings.sectionRequired')}</span>}
                        </label>
                        <span className={styles.sectionMoves}>
                          <button type="button" className={styles.sectionMove} disabled={index === 0}
                            aria-label={t('admin.settings.sectionMoveUp', { name })}
                            onClick={() => update({ sections: moveSection(form.sections, index, -1) })}>↑</button>
                          <button type="button" className={styles.sectionMove}
                            disabled={index === form.sections.length - 1}
                            aria-label={t('admin.settings.sectionMoveDown', { name })}
                            onClick={() => update({ sections: moveSection(form.sections, index, 1) })}>↓</button>
                        </span>
                      </li>
                    );
                  })}
                </ol>
              </fieldset>

              <div className={styles.field}>
                <label className={styles.checkboxRow}>
                  <input type="checkbox" checked={form.openingEnabled}
                    onChange={(e) => update({ openingEnabled: e.target.checked })} />
                  {t('admin.settings.opening')}
                </label>
                <span className={styles.hint}>{t('admin.settings.openingHint')}</span>
                {form.openingEnabled && options.openingStyles.length > 1 && (
                  <select id={fieldId('openingStyle')} className={inputClass(false)} value={form.openingStyle}
                    aria-label={t('admin.settings.openingStyle')} onChange={(e) => update({ openingStyle: e.target.value })}>
                    {options.openingStyles.map((style) => (
                      <option key={style} value={style}>{t(`admin.settings.openingStyleOption.${style}`)}</option>
                    ))}
                  </select>
                )}
              </div>
            </div>

            <StorePreview branding={previewBranding(form)} logoUrl={settings.branding.logoUrl}
              fallbackName={source.fallbackName ?? ''}
              texts={{
                displayName: form.displayName, announcement: form.announcement,
                seoDescription: form.seoDescription, defaultCulture: form.defaultCulture,
              }} />
          </div>
        </section>

        {/* ── اللغة والمنطقة ── */}
        <section className={styles.section} aria-labelledby="section-locale">
          <h3 id="section-locale" className={styles.sectionTitle}>{t('admin.settings.section.locale')}</h3>
          <fieldset className={styles.fieldset}>
            <legend className={styles.label}>{t('admin.settings.enabledCultures')}</legend>
            <div className={styles.choices} id={fieldId('enabledCultures')} tabIndex={-1}>
              {options.cultures.map((culture) => (
                <label key={culture} className={styles.choice}>
                  <input type="checkbox" checked={form.enabledCultures.includes(culture)}
                    onChange={(e) => update({
                      enabledCultures: e.target.checked
                        ? options.cultures.filter((c) => c === culture || form.enabledCultures.includes(c))
                        : form.enabledCultures.filter((c) => c !== culture),
                    })} />
                  {cultureLabel(culture)}
                </label>
              ))}
            </div>
            {fieldError('enabledCultures') && <span className={styles.fieldError}>{fieldError('enabledCultures')}</span>}
          </fieldset>
          <div className={styles.grid2}>
            <div className={styles.field}>
              <label className={styles.label} htmlFor={fieldId('defaultCulture')}>{t('admin.settings.defaultCulture')}</label>
              <select id={fieldId('defaultCulture')} className={inputClass(false)} value={form.defaultCulture}
                onChange={(e) => update({
                  defaultCulture: e.target.value,
                  // اللغة الافتراضية مفعّلة دائماً (Tenant.SetLocale) — تُضاف بدل أن يُرفض الحفظ.
                  enabledCultures: options.cultures.filter((c) => c === e.target.value || form.enabledCultures.includes(c)),
                })}>
                {options.cultures.map((culture) => <option key={culture} value={culture}>{cultureLabel(culture)}</option>)}
              </select>
            </div>
            <div className={styles.field}>
              <label className={styles.label} htmlFor={fieldId('timeZone')}>{t('admin.settings.timeZone')}</label>
              <input id={fieldId('timeZone')} className={inputClass(!!fieldError('timeZone'))} dir="ltr" list="settings-time-zones"
                value={form.timeZone} maxLength={options.limits.timeZone} onChange={(e) => update({ timeZone: e.target.value })}
                aria-invalid={!!fieldError('timeZone')} />
              <TimeZones />
              <span className={fieldError('timeZone') ? styles.fieldError : styles.hint}>
                {fieldError('timeZone') ?? t('admin.settings.timeZoneHint')}
              </span>
            </div>
          </div>
          <dl className={styles.readOnly}>
            <dt>{t('admin.settings.currency')}</dt>
            <dd><span dir="ltr">{locale.currency}</span> · {t(source.currencyHint ?? 'admin.settings.currencyHint')}</dd>
          </dl>
        </section>

        {/* ── التواصل ── */}
        <section className={styles.section} aria-labelledby="section-contact">
          <h3 id="section-contact" className={styles.sectionTitle}>{t('admin.settings.section.contact')}</h3>
          <p className={styles.sectionHint}>{t('admin.settings.section.contactHint')}</p>
          <div className={styles.grid2}>
            {[['contactEmail', 'email'], ['contactPhone', 'tel']].map(([field, type]) => (
              <div key={field} className={styles.field}>
                <label className={styles.label} htmlFor={fieldId(field)}>{t(`admin.settings.${field}`)}</label>
                <input id={fieldId(field)} type={type} dir="ltr" className={inputClass(!!fieldError(field))}
                  value={form[field]} onChange={(e) => update({ [field]: e.target.value })} aria-invalid={!!fieldError(field)}
                  autoComplete="off" />
                {fieldError(field) && <span className={styles.fieldError}>{fieldError(field)}</span>}
              </div>
            ))}
          </div>
          <h4 className={styles.subTitle}>{t('admin.settings.address')}</h4>
          <div className={styles.grid2}>{localizedFields('address', { multiline: true, max: options.limits.address })}</div>
        </section>

        {/* ── التواصل الاجتماعي ── */}
        <section className={styles.section} aria-labelledby="section-social">
          <h3 id="section-social" className={styles.sectionTitle}>{t('admin.settings.section.social')}</h3>
          <p className={styles.sectionHint}>{t('admin.settings.section.socialHint')}</p>
          <div id={fieldId('social')} tabIndex={-1} className={styles.socialList}>
            {form.social.map((link, index) => {
              const problem = fieldError(`social.${index}`);
              const domains = options.socialNetworks.find((n) => n.network === link.network)?.domains ?? [];
              return (
                // المفتاح موضع الصفّ: الروابط بلا معرّف، والترتيب لا يتبدّل إلا بالحذف.
                <div key={index} className={styles.socialRow}>
                  <select className={inputClass(false, styles.network)} value={link.network}
                    aria-label={t('admin.settings.socialNetwork')}
                    onChange={(e) => update({ social: form.social.map((l, i) => (i === index ? { ...l, network: e.target.value } : l)) })}>
                    {options.socialNetworks.map(({ network }) => (
                      <option key={network} value={network} disabled={network !== link.network && usedNetworks.has(network)}>
                        {t(`admin.settings.networks.${network}`)}
                      </option>
                    ))}
                  </select>
                  <input id={fieldId(`social.${index}`)} className={inputClass(!!problem, styles.socialUrl)} dir="ltr" type="url"
                    placeholder={`https://${domains[0] ?? ''}/`} value={link.url} aria-invalid={!!problem}
                    aria-label={t('admin.settings.socialUrl', { network: t(`admin.settings.networks.${link.network}`) })}
                    onChange={(e) => update({ social: form.social.map((l, i) => (i === index ? { ...l, url: e.target.value } : l)) })} />
                  <button type="button" className={styles.iconButton}
                    aria-label={t('admin.settings.removeLink', { network: t(`admin.settings.networks.${link.network}`) })}
                    onClick={() => update({ social: form.social.filter((_, i) => i !== index) })}>
                    <TrashIcon size={16} />
                  </button>
                  {problem && <span className={`${styles.fieldError} ${styles.socialError}`}>{problem}</span>}
                </div>
              );
            })}
          </div>
          {fieldError('social') && <span className={styles.fieldError}>{fieldError('social')}</span>}
          {nextNetwork && form.social.length < options.limits.socialLinks && (
            <Button variant="ghost" size="sm" type="button"
              onClick={() => update({ social: [...form.social, { network: nextNetwork, url: '' }] })}>
              {t('admin.settings.addLink')}
            </Button>
          )}
        </section>

        {/* ── السياسات ── */}
        {/* TD-42 (قرار C): روابط يستضيفها التاجر، لا صفحات تُؤلَّف هنا. التذييل يرسم المضبوط وحده. */}
        {/* خادمٌ لا يعلن أنواعاً (أقدم من هذه الميزة) ⇒ لا قسم، لا قسمٌ بلا حقول. */}
        {policyKinds.length > 0 && (
        <section className={styles.section} aria-labelledby="section-policies">
          <h3 id="section-policies" className={styles.sectionTitle}>{t('admin.settings.section.policies')}</h3>
          <p className={styles.sectionHint}>{t('admin.settings.section.policiesHint')}</p>
          <div className={styles.grid2}>
            {policyKinds.map((kind) => {
              const problem = fieldError(`policies.${kind}`);
              return (
                <div key={kind} className={styles.field}>
                  <label className={styles.label} htmlFor={fieldId(`policies.${kind}`)}>
                    {t(`admin.settings.policies.${kind}`)}
                  </label>
                  <input id={fieldId(`policies.${kind}`)} type="url" dir="ltr" className={inputClass(!!problem)}
                    placeholder="https://" value={form.policies?.[kind] ?? ''} aria-invalid={!!problem}
                    autoComplete="off"
                    onChange={(e) => update({ policies: { ...form.policies, [kind]: e.target.value } })} />
                  <span className={problem ? styles.fieldError : styles.hint}>
                    {problem ?? t('admin.settings.policies.hint', { max: options.limits.policyUrl })}
                  </span>
                </div>
              );
            })}
          </div>
        </section>
        )}

        {/* ── محرّكات البحث ── */}
        <section className={styles.section} aria-labelledby="section-seo">
          <h3 id="section-seo" className={styles.sectionTitle}>{t('admin.settings.section.seo')}</h3>
          <p className={styles.sectionHint}>{t('admin.settings.section.seoHint')}</p>
          <h4 className={styles.subTitle}>{t('admin.settings.seoTitle')}</h4>
          <div className={styles.grid2}>{localizedFields('seoTitle', { max: options.limits.seoTitle })}</div>
          <h4 className={styles.subTitle}>{t('admin.settings.seoDescription')}</h4>
          <div className={styles.grid2}>
            {localizedFields('seoDescription', { multiline: true, max: options.limits.seoDescription })}
          </div>
        </section>

        {/* ── شريط الإعلان ── */}
        <section className={styles.section} aria-labelledby="section-announcement">
          <h3 id="section-announcement" className={styles.sectionTitle}>{t('admin.settings.section.announcement')}</h3>
          <p className={styles.sectionHint}>{t('admin.settings.section.announcementHint')}</p>
          <div className={styles.grid2}>{localizedFields('announcement', { max: options.limits.announcement })}</div>
        </section>

        <div className={styles.saveBar} role="region" aria-label={t('admin.settings.saveBar')}>
          <span className={styles.saveState} aria-live="polite">
            {dirty ? t('admin.settings.unsaved') : t('admin.settings.upToDate')}
          </span>
          <div className={styles.saveActions}>
            {actions}
            <Button variant="ghost" type="button" disabled={!dirty || busy}
              onClick={() => { setDraft(null); setSubmitted(false); setServerError(null); }}>
              {t('admin.settings.discard')}
            </Button>
            <Button variant="primary" type="submit" loading={busy} disabled={!dirty}>{t('common.save')}</Button>
          </div>
        </div>
      </form>
    </div>
  );
}

// اقتراحات المناطق من المتصفّح نفسه (Intl) — لا قائمة مكتوبة هنا. متصفّح لا يعرفها يترك الحقل حرّاً.
export function browserTimeZones() {
  try { return typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []; }
  catch { return []; }
}

export function TimeZones() {
  return (
    <datalist id="settings-time-zones">
      {['UTC', ...browserTimeZones().filter((zone) => zone !== 'UTC')].map((zone) => <option key={zone} value={zone} />)}
    </datalist>
  );
}
