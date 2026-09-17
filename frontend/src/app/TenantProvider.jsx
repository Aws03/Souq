import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { setLanguage } from '../i18n';
import { BootScreen } from './BootScreens';
import { applyStoreTheme } from './storeTheme';
import { setStoreDateSettings } from './dateLocale';
import { bootModeForConfig, bootOutcome, isModuleEnabled, setStoreCurrency, supportedLanguage } from './tenantModel';
import { oppositeMode, readStoredMode, resolveThemeMode, systemPrefersDark, writeStoredMode } from './themeMode';

// ============================================================================
// TenantProvider (المرحلة 15، WhiteLabel.md §3، ADR-0035): أول ما يطلبه التطبيق على أيّ مضيف هو إعداد متجره — الهوية واللغات
// والعملة والوحدات — فيرسم البناء نفسه أيّ متجر. المضيف وحده يحدّد المتجر (الخادم يحلّه)؛ الواجهة لا ترسل معرّف متجر أبداً.
//   store    ⇒ التطبيق بهوية المتجر.
//   platform ⇒ منطقة المنصّة (نقطة المتجر غير موجودة على مضيف المنصّة).
//   closed / unknown / error ⇒ شاشة الإقلاع المناسبة.
// الخادم يفرض كل شيء في كل الأحوال — هذا عرض لا حماية.
// ============================================================================
const TenantContext = createContext({ mode: 'loading', config: null, retry: () => {}, refresh: async () => {} });

// سياق مستقلّ للسمة: مكوّن يبدّل الوضع لا يجب أن يُعيد رسم كل قارئ لإعداد المتجر، والعكس.
const ThemeContext = createContext({ theme: 'light', toggleTheme: () => {}, storePrefersTheme: 'system' });

export function TenantProvider({ children }) {
  const { i18n } = useTranslation();
  const [state, setState] = useState({ mode: 'loading', config: null });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    api.getStorefrontConfig()
      .then((config) => {
        if (!active) return;
        // قبل أول رسم: عملة الأسعار بلا عملة صريحة، وموضع التاريخ ومنطقته، ولغة يفعّلها المتجر.
        setStoreCurrency(config.settings?.locale?.currency);
        setStoreDateSettings({
          culture: config.settings?.locale?.defaultCulture,
          timeZone: config.settings?.locale?.timeZone,
        });
        const language = supportedLanguage(config, i18n.language);
        if (language !== i18n.language) setLanguage(language);
        // متجر مغلق يردّ إعداده بنجاح (R-08): الحالة تقرّر الشاشة، والإعداد يبقى كي تحمل شاشة الإغلاق هويّته.
        setState({ mode: bootModeForConfig(config), config });
      })
      .catch((error) => { if (active) setState({ mode: bootOutcome(error), config: null }); });
    return () => { active = false; };
    // i18n.language مقصود خارج الاعتماديات: الإقلاع مرة لكل محاولة، وتبديل اللغة يعالجه الأثر التالي.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attempt]);

  // ========================================================================
  // الوضع (فاتح/داكن): اختيار الزائر إن وُجد، وإلا تفضيل المتجر، وإلا تفضيل نظام الزائر.
  // يُحسم قبل أول رسم (useState بدالة) كي لا تومض الصفحة بيضاء ثم تسودّ.
  // ========================================================================
  const [chosenTheme, setChosenTheme] = useState(readStoredMode);
  const storePrefersTheme = state.config?.settings?.branding?.themeMode ?? 'system';
  const theme = resolveThemeMode({
    stored: chosenTheme,
    storePreference: storePrefersTheme,
    systemPrefersDark: systemPrefersDark(),
  });

  // ========================================================================
  // الوضع على المستند **قبل** وصول إعداد المتجر.
  //
  // إعداد المتجر يحتاج رحلة شبكة، وحتى تصل لا يحمل المستند وضعاً — فيرسم المتصفّح برموز
  // :root المحايدة (فاتحة) ثم يسودّ فجأةً. زائرٌ اختار الداكن يرى وميضاً أبيض في كل تحميل.
  //
  // واختيار الزائر متاح فوراً (من تخزينه المحلّي)، فيُطبَّق الآن ويُكمَّل بألوان المتجر لاحقاً.
  // ========================================================================
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.style.colorScheme = theme;
  }, [theme]);

  // الهوية على المستند، وتتبع تبديل اللغة (العنوان والوصف بلغة الزائر) والوضع. تُطبَّق كلّما وُجد إعداد — بما فيه متجر
  // مغلق، كي تظهر شاشة إغلاقه بألوانه وخطّه لا بمظهر محايد.
  useEffect(() => {
    if (state.config) applyStoreTheme(state.config, i18n.language, theme);
  }, [state, i18n.language, theme]);

  // زائر لم يختر شيئاً يتبع نظامه حيّاً: تبديل النظام ليلاً يجب أن يتبعه المتجر بلا إعادة تحميل.
  // العدّاد لا معنى له في ذاته — وجوده وحده يُعيد التقييم، فـsystemPrefersDark() تُقرأ عند الرسم.
  const [, bumpSystemTick] = useState(0);
  useEffect(() => {
    if (chosenTheme || typeof window.matchMedia !== 'function') return undefined;
    const query = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => bumpSystemTick((n) => n + 1);
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, [chosenTheme]);

  const retry = useCallback(() => {
    setState({ mode: 'loading', config: null });
    setAttempt((n) => n + 1);
  }, []);

  // إعادة قراءة الإعداد بعد تعديله من لوحة الإدارة — بلا حالة "loading": تلك تستبدل التطبيق كلّه بشاشة
  // الإقلاع، فيفقد المدير نموذجه وموضعه لمجرّد أنه حفظ لوناً. فشلها لا يُسقط شيئاً: الإعداد القديم يبقى
  // معروضاً، والحفظ نفسه نجح على الخادم.
  const refresh = useCallback(() => api.getStorefrontConfig()
    .then((config) => {
      setStoreDateSettings({
        culture: config.settings?.locale?.defaultCulture,
        timeZone: config.settings?.locale?.timeZone,
      });
      setState({ mode: bootModeForConfig(config), config });
    })
    .catch(() => {}), []);

  const value = useMemo(() => ({ ...state, retry, refresh }), [state, retry, refresh]);
  const themeValue = useMemo(() => ({
    theme,
    storePrefersTheme,
    // التبديل اختيار صريح: يُحفظ، فيغلب تفضيل المتجر ونظام الزائر من الآن فصاعداً.
    toggleTheme: () => setChosenTheme((current) => {
      const next = oppositeMode(resolveThemeMode({
        stored: current, storePreference: storePrefersTheme, systemPrefersDark: systemPrefersDark(),
      }));
      writeStoredMode(next);
      return next;
    }),
  }), [theme, storePrefersTheme]);

  const ready = state.mode === 'store' || state.mode === 'platform';

  return (
    <TenantContext.Provider value={value}>
      <ThemeContext.Provider value={themeValue}>
        {ready ? children : <BootScreen mode={state.mode} config={state.config} onRetry={retry} />}
      </ThemeContext.Provider>
    </TenantContext.Provider>
  );
}

export const useTheme = () => useContext(ThemeContext);

export const useTenant = () => useContext(TenantContext);

export const useStoreConfig = () => useContext(TenantContext).config;

// واجهة وحدة معطّلة تُخفى (الخادم يرفض نقاطها بـ 404 ModuleDisabled مهما كان).
export const useModule = (module) => isModuleEnabled(useContext(TenantContext).config, module);
