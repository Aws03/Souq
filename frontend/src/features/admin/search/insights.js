// ============================================================================
// حساب أرقام شاشة أثر البحث (M13) — منطق خالص مُختبَر، خارج المكوّن.
//
// الحساب الوحيد هنا نسبةٌ، وهي بالضبط نوع الرقم الذي أخطأ M12 في أربعةٍ من أمثاله في لوحة التقارير:
// قاسمٌ صفر، أو مقامٌ هو مجموع الصفحة لا مجموع النافذة. فموضعه دالّةٌ تُختبر لا سطرٌ في JSX.
// ============================================================================

/**
 * نسبة البحوث التي لم تجد شيئاً، من كل بحوث النافذة.
 *
 * `null` حين لا بحث في النافذة — **لا صفر**: الصفر دعوى ("لا شيء يفشل") والواقع أنّه لا شيء يُقاس،
 * والشاشة تعرض شرطةً على الأولى ورقماً مطمئناً كاذباً على الثانية.
 *
 * @param {{totalSearches?: number, zeroResultSearches?: number}|null|undefined} summary
 * @returns {number|null} النسبة من 100، أو null إن لا قياس.
 */
export function zeroResultShare(summary) {
  const total = summary?.totalSearches ?? 0;
  if (total <= 0) return null;

  const zero = summary?.zeroResultSearches ?? 0;
  return (zero / total) * 100;
}

// ============================================================================
// حالة الكلمة كما تُعرض: ثلاثٌ لا اثنتان.
//
// الفرق الذي كان ضائعاً: الكلمة التي **لم تفشل ولا مرّة** ليست "فشلت صفر مرّة". عرضُها بنغمة تحذيرٍ
// ونصٍّ بعدّاد يقول للتاجر إنّ عليه عملاً في كلمةٍ تعمل تماماً — وفي العربية يُنتج ذلك صيغةَ جمعٍ لعددٍ
// صفر أصلاً. فالحالات ثلاث، ولكلٍّ نغمتها:
//   • لم تجد شيئاً أبداً  ⇒ خطر: مرادفٌ أو منتجٌ ناقص، وهو العمل.
//   • تفشل أحياناً        ⇒ تحذير: غالباً نفاد مخزونٍ مؤقّت، يُنظر فيه ولا يُستعجل.
//   • تجد دائماً          ⇒ نجاح: لا عمل. تظهر فقط حين يُطفئ التاجر المرشّح.
// ============================================================================

/**
 * @param {{searches?: number, zeroResultSearches?: number, neverFoundAnything?: boolean}} row
 * @returns {{tone: 'danger'|'warning'|'success', key: string, count: number}}
 */
export function insightOutcome(row) {
  const failures = row?.zeroResultSearches ?? 0;

  if (row?.neverFoundAnything) return { tone: 'danger', key: 'foundNothing', count: failures };
  if (failures > 0) return { tone: 'warning', key: 'foundSometimes', count: failures };
  return { tone: 'success', key: 'foundAlways', count: 0 };
}
