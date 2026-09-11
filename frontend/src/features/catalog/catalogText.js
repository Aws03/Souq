// ============================================================================
// نصوص الكتالوج لكل لغة (المرحلة 5، D-10). الـ API يعيد translations = { ar: { name, description, ... }, en: ... }
// إلى جانب name/description بلغة المتجر الافتراضية: الواجهة تعرض لغتها إن وُجدت وإلا الافتراضية — بلا طلب جديد عند
// تبديل اللغة. ونموذج الإدارة يحوّل الترجمات إلى حقول ويعيدها كما يقبلها الخادم. منطق خالص مُختبَر بـ Vitest.
// ============================================================================
export const CATALOG_CULTURES = ['ar', 'en'];

export function localizedText(entity, lang, field = 'name') {
  if (!entity) return '';
  return entity.translations?.[lang]?.[field] ?? legacyText(entity, lang, field) ?? entity[field] ?? '';
}

export const localizedName = (entity, lang) => localizedText(entity, lang, 'name');
export const localizedDescription = (entity, lang) => localizedText(entity, lang, 'description');

// عناصر سلة/مفضّلة محفوظة في localStorage قبل المرحلة 5 تحمل nameAr/nameEn بدل translations.
function legacyText(entity, lang, field) {
  if (field !== 'name') return undefined;
  return lang === 'ar' ? entity.nameAr : (entity.nameEn ?? entity.nameAr);
}

// ترجمات الخادم ⇒ حقول النموذج لكل لغة مدعومة (عنوان/وصف SEO يُحفظان كما هما وإن لم تعرضهما الشاشة).
export function textsToForm(translations) {
  return Object.fromEntries(CATALOG_CULTURES.map((culture) => {
    const text = translations?.[culture];
    return [culture, {
      name: text?.name ?? '',
      description: text?.description ?? '',
      metaTitle: text?.metaTitle ?? null,
      metaDescription: text?.metaDescription ?? null,
    }];
  }));
}

// حقول النموذج ⇒ translations: لغة بلا اسم لا تُرسل (والخادم يحذف ترجمتها)، والنص مقصوص، والفارغ null.
export function formToTexts(form) {
  const texts = {};
  for (const culture of CATALOG_CULTURES) {
    const text = form?.[culture];
    const name = text?.name?.trim();
    if (!name) continue;
    texts[culture] = {
      name,
      description: text.description?.trim() || null,
      metaTitle: text.metaTitle?.trim() || null,
      metaDescription: text.metaDescription?.trim() || null,
    };
  }
  return texts;
}

export const hasAnyName = (form) => Object.keys(formToTexts(form)).length > 0;
