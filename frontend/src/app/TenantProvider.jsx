import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { setLanguage } from '../i18n';
import { BootScreen } from './BootScreens';
import { applyStoreTheme } from './storeTheme';
import { setStoreDateSettings } from './dateLocale';
import { bootModeForConfig, bootOutcome, isModuleEnabled, setStoreCurrency, supportedLanguage } from './tenantModel';

// ============================================================================
// TenantProvider (المرحلة 15، WhiteLabel.md §3، ADR-0035): أول ما يطلبه التطبيق على أيّ مضيف هو إعداد متجره — الهوية واللغات
// والعملة والوحدات — فيرسم البناء نفسه أيّ متجر. المضيف وحده يحدّد المتجر (الخادم يحلّه)؛ الواجهة لا ترسل معرّف متجر أبداً.
//   store    ⇒ التطبيق بهوية المتجر.
//   platform ⇒ منطقة المنصّة (نقطة المتجر غير موجودة على مضيف المنصّة).
//   closed / unknown / error ⇒ شاشة الإقلاع المناسبة.
// الخادم يفرض كل شيء في كل الأحوال — هذا عرض لا حماية.
// ============================================================================
const TenantContext = createContext({ mode: 'loading', config: null, retry: () => {} });

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

  // الهوية على المستند، وتتبع تبديل اللغة (العنوان والوصف بلغة الزائر). تُطبَّق كلّما وُجد إعداد — بما فيه متجر مغلق،
  // كي تظهر شاشة إغلاقه بألوانه وخطّه لا بمظهر محايد.
  useEffect(() => {
    if (state.config) applyStoreTheme(state.config, i18n.language);
  }, [state, i18n.language]);

  const retry = useCallback(() => {
    setState({ mode: 'loading', config: null });
    setAttempt((n) => n + 1);
  }, []);

  const value = useMemo(() => ({ ...state, retry }), [state, retry]);
  const ready = state.mode === 'store' || state.mode === 'platform';

  return (
    <TenantContext.Provider value={value}>
      {ready ? children : <BootScreen mode={state.mode} config={state.config} onRetry={retry} />}
    </TenantContext.Provider>
  );
}

export const useTenant = () => useContext(TenantContext);

export const useStoreConfig = () => useContext(TenantContext).config;

// واجهة وحدة معطّلة تُخفى (الخادم يرفض نقاطها بـ 404 ModuleDisabled مهما كان).
export const useModule = (module) => isModuleEnabled(useContext(TenantContext).config, module);
