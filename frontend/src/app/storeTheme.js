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
  // القالبُ مُدخَلٌ للاشتقاق لا سمةٌ تُكتب وحسب (C8): ظلُّ الوضع الفاتح مصبوغٌ بلون الهوية،
  // فهو من الرموز التي تُكتب سطرياً — ولو تُرك للورقة لَغلَبه السطريُّ وبقي القالبان يطفوان.
  const preset = branding?.themePreset ?? 'classic';
  for (const [name, value] of Object.entries(themeVariables(branding, mode, preset))) {
    root.style.setProperty(name, value);
  }
  root.dataset.preset = preset;
  root.dataset.theme = mode;
  // حقول النماذج وأشرطة التمرير التي يرسمها المتصفّح نفسه تتبع هذه الخاصّية لا متغيّراتنا.
  root.style.colorScheme = mode;

  document.title = documentTitle(config, language);
  setMeta('description', documentDescription(config, language));
  if (branding?.faviconUrl) setLink('icon', branding.faviconUrl);
  setStylesheet('store-fonts', fontStylesheetUrl(branding?.typography));
}

// ============================================================================
// مضيف المنصّة لا متجر له ولا هوية: رموزه تُشتقّ من اللوحة المحايدة (themeVariables بلا هوية) للوضع الحالي.
//
// كان المضيف يعتمد على رموز styles.css وحدها، وكتلتها الداكنة لا تحمل إلا الأسطح والنصّ — تغطية لما قبل وصول
// إعداد متجر، لا نظاماً كاملاً. فمالك منصّة في وضع داكن كان سيرى الأساسي الفاتح (#1F2937) على خلفية داكنة،
// وشارات حالة بلا أسطحها. الاشتقاق نفسه الذي يعطي كل متجر وضعيه يعطيهما للمنصّة، بلا قائمة ألوان ثانية.
// ============================================================================
export function applyPlatformTheme(mode = 'light') {
  const root = document.documentElement;
  for (const [name, value] of Object.entries(themeVariables(null, mode))) root.style.setProperty(name, value);
  delete root.dataset.preset;
  root.dataset.theme = mode;
  root.style.colorScheme = mode;
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
