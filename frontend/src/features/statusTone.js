// ============================================================================
// نغمة الحالة: مفردةٌ واحدة لكل ما يُعرض شارةً في هذا التطبيق (TD-28، وُحِّد في M10).
//
// **ما كان قبل هذا.** تسع خرائط مسمّاة، وثماني ثلاثيّات مكتوبة في مكانها، وستّ قراءات ضمنيّة من
// `status.toLowerCase()` — في أربع وحدات CSS، ثلاثٌ منها **نسخٌ متطابقة حرفاً بحرف** من القواعد
// الخمس نفسها. اثنتان وعشرون طبقةً تنهار كلّها إلى خمس نغمات حقيقيّة. وأثرُ ذلك لم يكن نظريّاً:
//
//   • `Succeeded` كانت تُلوَّن أزرق دفعةً وأخضر استرداداً — **في الدرج نفسه، على بُعد أسطر**.
//   • "مُفعَّل" كان أزرق في الكوبونات وطرق الشحن، وأخضر في الفئات والمتغيّرات: أربعة جداول، معنى
//     واحد، لونان.
//   • `invited` كانت عنبريّة في شاشة الفريق وزرقاء في شاشة حسابات المنصّة — **من الدالّة نفسها**.
//   • و"بانتظار الدفع" كانت تُقرأ بدرجتَي تباين مختلفتين: تصحيحُ AA طُبِّق على وحدة الإدارة وحدها.
//
// **ما تقرّر هنا، وما لم يُقرَّر.** وُحِّدت المفردة وأُصلحت التناقضات — أي المواضع التي تُعطي فيها
// شاشتان **جوابين مختلفين للقيمة نفسها**، وهي عيوب بحكم التعريف. أمّا نغمةُ كلّ حالةٍ متّسقةٍ مع
// نفسها فبقيت كما هي: تغييرها إعادةُ تصميم لا توحيد، وهذه المرحلة ليست إعادة تصميم.
//
// وحيثما تناقضت الشاشتان، فاز الجواب الذي تقوله الأغلبية:
//   • `invited` ⇒ **تحذير**. هي الحالة الوحيدة في المجموعة التي تنتظر فعلَ إنسانٍ قبل أن يصير
//     الحساب صالحاً، والأزرق محمَّلٌ أصلاً بمعنى "النظام يعمل" (`Provisioning`).
//   • "مُفعَّل" ⇒ **نجاح**. الأخضر يعني "قائم/متاح" في المنتج والفئة والمتغيّر والموظّف والمتجر
//     والعميل؛ الكوبونات وطرق الشحن كانتا الشاذّتين.
//
// النغمات خمس، ولكلٍّ زوجُ رموزٍ واحد (نصّ + سطح ناعم) يُشتقّ للوضع الفاتح والداكن — وبينها
// **neutral**، وهي العضو الذي كان غائباً عن مفردة الشارات كلّها.
// ============================================================================

/** @typedef {'success' | 'info' | 'warning' | 'danger' | 'neutral'} Tone */

/** كل النغمات، بترتيب "كل شيء تمام" إلى "توقّف" ثم المحايدة. */
export const TONES = /** @type {const} */ (['success', 'info', 'warning', 'danger', 'neutral']);

// ── المفردات، كلٌّ بقيمه كما يرسلها الخادم ──────────────────────────────────
// المفتاح هو القيمة كما تأتي من الـ API بحرفها (Pending لا pending): لا تطبيع ضمني يخفي قيمة جديدة.
const VOCABULARIES = {
  // دورة حياة الطلب. `Paid` تبقى على ما كانت عليه: نغمتها معلومةٌ مقروءة، وتحويلها قرار تصميم.
  order: { Pending: 'warning', Paid: 'info', Shipped: 'info', Delivered: 'success', Cancelled: 'danger' },

  // الدفعة والاسترداد. `Succeeded` واحدة في الاثنتين الآن — كانت تختلف بينهما في ملفّ واحد.
  payment: { Pending: 'warning', Succeeded: 'success', Failed: 'danger', Cancelled: 'danger' },
  refund: { Pending: 'warning', Succeeded: 'success', Failed: 'danger' },

  review: { Pending: 'warning', Approved: 'success', Rejected: 'danger' },

  // `Draft` تبقى تحذيراً: المحايد أدقّ دلالةً (ليس مشكلة، بل لم يُنشَر بعد) لكنّ نقله تغييرُ مظهرٍ
  // لشاشتين بلا عيبٍ يدفعه. مرشَّحٌ صريح لقرار تصميم لاحق.
  product: { Active: 'success', Draft: 'warning', Archived: 'danger' },

  // `Released` تبقى خطراً للسبب نفسه: التحرير هو النهاية **الطبيعية** لسلّة مهجورة لا فشلٌ، والمحايد
  // أصدق — لكنّه تغيير مظهر، لا تصحيح تناقض.
  couponRedemption: { Reserved: 'warning', Confirmed: 'success', Released: 'danger' },

  // حساب موظّف أو حساب منصّة. هنا كان التناقض الصريح: عنبريّة هنا وزرقاء هناك.
  account: { active: 'success', invited: 'warning', disabled: 'danger' },

  store: { Provisioning: 'info', Active: 'success', Suspended: 'danger', Archived: 'danger' },

  customer: { Active: 'success', Blocked: 'danger' },

  // بنود جاهزية المتجر — وفيها وحدها كانت المحايدة مستعملة أصلاً.
  readiness: { done: 'success', attention: 'info', missing: 'danger', blocked: 'neutral' },

  // مستوى المخزون: مقياس ترتيبي حقيقي، فينطبق على النغمات بلا تكلّف.
  stockLevel: { ok: 'success', warn: 'warning', low: 'danger' },
};

/**
 * نغمة قيمةٍ في مفردتها. قيمة لا تعرفها المفردة ⇒ محايدة: شارةٌ بلا لونٍ مفهوم أصدق من لونٍ مخترع،
 * ولا ترمي — حالةٌ جديدة من الخادم لا تُسقط شاشة.
 * @param {string} vocabulary اسم المفردة؛ اسمٌ غير معروف محايد أيضاً (لا يرمي)
 * @param {string | null | undefined} value
 * @returns {Tone}
 */
export function statusTone(vocabulary, value) {
  return VOCABULARIES[vocabulary]?.[String(value)] ?? 'neutral';
}

/**
 * نغمة علَمٍ منطقي (مُفعَّل / معطَّل) — أكثر ما كان يُكتب ثلاثيّةً في مكانه، بأربعة أجوبة مختلفة.
 * @param {boolean | null | undefined} on
 * @returns {Tone}
 */
export const flagTone = (on) => (on ? 'success' : 'danger');

/**
 * مستوى المخزون من المتاح وحدّ التنبيه — القاعدة نفسها التي يستعملها الخادم في `isLowStock`.
 * @param {{ available: number, lowStockThreshold: number }} item
 * @returns {Tone}
 */
export function stockTone({ available, lowStockThreshold }) {
  if (available <= lowStockThreshold) return statusTone('stockLevel', 'low');
  if (available <= lowStockThreshold * 2) return statusTone('stockLevel', 'warn');
  return statusTone('stockLevel', 'ok');
}

/** أسماء المفردات — لاختبارٍ يمرّ على كلّها، وللتأكّد من عدم بقاء خريطةٍ خارج هذا الملفّ. */
export const VOCABULARY_NAMES = Object.keys(VOCABULARIES);

/** قيم مفردةٍ ما، لاختبارها كلّها بلا نسخ القائمة. */
/** @param {string} vocabulary */
export const valuesOf = (vocabulary) => Object.keys(VOCABULARIES[vocabulary] ?? {});
