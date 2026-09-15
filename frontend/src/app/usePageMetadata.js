import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useTenant } from './TenantProvider';
import { documentDescription, documentTitle } from './tenantModel';
import { canonicalUrl, pageTitle, robotsFor, socialTags } from './pageMetadata';

// ============================================================================
// يضبط عنوان الصفحة ووصفها ورابطها القانوني ووسوم مشاركتها، ويعيد افتراضيات المتجر عند
// مغادرتها. يُستدعى من الصفحة نفسها لأنها وحدها تعرف اسم منتجها أو فئتها.
// الوسوم تُكتب مباشرةً على <head>: لا عرض من الخادم في هذا التطبيق، وزاحف لا ينفّذ
// JavaScript لن يراها — وهذا حدّ معماري موثّق لا يُخفى (FrontendArchitecture).
// ============================================================================
export function usePageMetadata({ title, description, image, type, robots } = {}) {
  const { config } = useTenant();
  const { i18n } = useTranslation();
  const location = useLocation();
  const language = i18n.language;

  useEffect(() => {
    const storeTitle = documentTitle(config, language);
    const storeDescription = documentDescription(config, language);
    const branding = config?.settings?.branding;

    const resolvedTitle = pageTitle(title, storeTitle);
    const resolvedDescription = description || storeDescription;
    const url = canonicalUrl(window.location.origin, location.pathname, location.search);

    document.title = resolvedTitle;
    setMeta('name', 'description', resolvedDescription);
    setMeta('name', 'robots', robots ?? robotsFor(location.pathname));
    setCanonical(url);
    for (const [property, content] of socialTags({
      title: resolvedTitle,
      description: resolvedDescription,
      image: image || branding?.socialImageUrl,
      url,
      type,
    })) setMeta('property', property, content);

    return () => {
      // العودة إلى هوية المتجر: صفحة تالية بلا بيانات خاصّة يجب ألّا ترث بيانات سابقتها.
      document.title = storeTitle;
      setMeta('name', 'description', storeDescription);
      setMeta('name', 'robots', null);
      setCanonical(null);
      for (const property of ['og:title', 'og:description', 'og:image', 'og:url', 'og:type', 'twitter:card'])
        setMeta('property', property, null);
    };
  }, [config, language, location.pathname, location.search, title, description, image, type, robots]);
}

function setMeta(attribute, key, content) {
  const selector = `meta[${attribute}="${key}"]`;
  let element = document.head.querySelector(selector);
  if (!content) {
    element?.remove();
    return;
  }
  if (!element) {
    element = document.createElement('meta');
    element.setAttribute(attribute, key);
    document.head.append(element);
  }
  element.setAttribute('content', content);
}

function setCanonical(href) {
  let element = document.head.querySelector('link[rel="canonical"]');
  if (!href) {
    element?.remove();
    return;
  }
  if (!element) {
    element = document.createElement('link');
    element.rel = 'canonical';
    document.head.append(element);
  }
  element.href = href;
}
