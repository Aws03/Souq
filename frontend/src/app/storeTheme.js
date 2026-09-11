import { documentDescription, documentTitle, fontStylesheetUrl, themeVariables } from './tenantModel';

// ============================================================================
// applyStoreTheme في وقت التشغيل (المرحلة 15، A7): يطبّق هوية المتجر على المستند — متغيّرات التصميم على <html>، والقالب (data-preset)،
// وعنوان الصفحة ووصفها، والأيقونة، وخطّ المتجر. الأثر الجانبي الوحيد للهوية؛ الحساب كله في tenantModel المُختبَر. الزائر لا يختار
// سمة: الهوية قرار المتجر.
// ============================================================================
export function applyStoreTheme(config, language) {
  const root = document.documentElement;
  const branding = config.settings?.branding;
  for (const [name, value] of Object.entries(themeVariables(branding))) root.style.setProperty(name, value);
  root.dataset.preset = branding?.themePreset ?? 'classic';

  document.title = documentTitle(config, language);
  setMeta('description', documentDescription(config, language));
  if (branding?.faviconUrl) setLink('icon', branding.faviconUrl);
  setStylesheet('store-fonts', fontStylesheetUrl(branding?.typography));
}

function setMeta(name, content) {
  let element = document.head.querySelector(`meta[name="${name}"]`);
  if (!content) {
    element?.remove();
    return;
  }
  if (!element) {
    element = document.createElement('meta');
    element.name = name;
    document.head.append(element);
  }
  element.content = content;
}

function setLink(rel, href) {
  let element = document.head.querySelector(`link[rel="${rel}"]`);
  if (!element) {
    element = document.createElement('link');
    element.rel = rel;
    document.head.append(element);
  }
  element.href = href;
}

function setStylesheet(id, href) {
  let element = document.getElementById(id);
  if (!element) {
    element = document.createElement('link');
    element.id = id;
    element.rel = 'stylesheet';
    document.head.append(element);
  }
  if (element.getAttribute('href') !== href) element.setAttribute('href', href);
}
