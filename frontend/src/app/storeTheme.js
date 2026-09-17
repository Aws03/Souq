import { documentDescription, documentTitle, fontStylesheetUrl, themeVariables } from './tenantModel';

// ============================================================================
// applyStoreTheme في وقت التشغيل (المرحلة 15، A7): يطبّق هوية المتجر على المستند — متغيّرات التصميم على <html>، والقالب (data-preset)،
// وعنوان الصفحة ووصفها، والأيقونة، وخطّ المتجر. الأثر الجانبي الوحيد للهوية؛ الحساب كله في tenantModel المُختبَر.
//
// الوضع (فاتح/داكن) مُعامل هنا لأنه مُدخَل لاشتقاق الرموز لا تجاوز لها: هذه المتغيّرات تُكتب
// سطرياً على <html>، والسطري يعلو أي قاعدة CSS — فقاعدة `[data-theme="dark"]` كانت ستُغلَب.
//
// data-theme يُكتب مع ذلك، لا ليُلوّن بل ليُستعمل في ثلاثة مواضع لا تصلها المتغيّرات:
// color-scheme للمتصفّح (حقول النماذج وأشرطة التمرير)، واستثناءات نادرة، والاختبارات.
// ============================================================================
export function applyStoreTheme(config, language, mode = 'light') {
  const root = document.documentElement;
  const branding = config.settings?.branding;
  for (const [name, value] of Object.entries(themeVariables(branding, mode))) root.style.setProperty(name, value);
  root.dataset.preset = branding?.themePreset ?? 'classic';
  root.dataset.theme = mode;
  // حقول النماذج وأشرطة التمرير التي يرسمها المتصفّح نفسه تتبع هذه الخاصّية لا متغيّراتنا.
  root.style.colorScheme = mode;

  document.title = documentTitle(config, language);
  setMeta('description', documentDescription(config, language));
  if (branding?.faviconUrl) setLink('icon', branding.faviconUrl);
  setStylesheet('store-fonts', fontStylesheetUrl(branding?.typography));
}

// خطّ يُعاين قبل أن يُحفظ (محرّر الإعدادات): عنصر مستقلّ عن خطّ المتجر الفعلي، كي لا يُبدَّل خطّ اللوحة
// نفسها بمجرّد تجربة خيار — وخطّ المتجر يبقى محمّلاً ما دام هو المحفوظ.
export function loadPreviewFonts(typography) {
  setStylesheet('store-fonts-preview', fontStylesheetUrl(typography));
}

function setMeta(name, content) {
  let element = /** @type {HTMLMetaElement|null} */ (document.head.querySelector(`meta[name="${name}"]`));
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
  let element = /** @type {HTMLLinkElement|null} */ (document.head.querySelector(`link[rel="${rel}"]`));
  if (!element) {
    element = document.createElement('link');
    element.rel = rel;
    document.head.append(element);
  }
  element.href = href;
}

function setStylesheet(id, href) {
  let element = /** @type {HTMLLinkElement|null} */ (document.getElementById(id));
  if (!element) {
    element = document.createElement('link');
    element.id = id;
    element.rel = 'stylesheet';
    document.head.append(element);
  }
  if (element.getAttribute('href') !== href) element.setAttribute('href', href);
}
