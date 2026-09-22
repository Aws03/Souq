// ============================================================================
// منطقُ شاشات الضريبة، نقيٌّ ومُختبَر وحده ([ADR-0055](0055)، قرار المالك P-06).
//
// **ولا شيء هنا يُفسّر قاعدةَ ضريبة.** ما هنا: تحويلُ نقاط الأساس إلى نسبةٍ يقرؤها إنسان،
// وترتيبُ الإصدارات، ومعرفةُ أيُّها نافذ، وصياغةُ ما يمنع الجمع. والقيمُ نفسها بياناتٌ يُدخلها
// مهنيّ ويؤكّدها — لا تُشتَقّ هنا ولا تُصحَّح ولا يُفترض لها افتراض.
// ============================================================================

/** الحدُّ الأعلى لنقاط الأساس: 10000 = 100%. النطاقُ نفسه الذي يفرضه `TaxRate`. */
export const MAX_BASIS_POINTS = 10_000;

/**
 * النسبةُ كما يقرؤها إنسان، من نقاط الأساس: 1600 ⇒ 16. **اشتقاقٌ لا تخزين** — الخادم يحفظ
 * النقاط وحدها، فلا يوجد رقمان يمكن أن يفترقا.
 * @param {number} basisPoints
 */
export const percentOf = (basisPoints) => Number(basisPoints) / 100;

/**
 * والعكس، لِما يُدخله المشغّل. يُقرَّب إلى أقرب نقطةِ أساس: كسرُ نقطةٍ لا تمثّله القاعدة، وتمريرُه
 * يجعل الخادم يرفض بما يبدو خطأً غامضاً.
 * @param {unknown} percent
 */
export function basisPointsOf(percent) {
  // حقلٌ فارغ ليس صفراً: `Number('')` تساوي صفراً، فبلا هذا الفحص تصير نسبةٌ لم تُدخَل بعد
  // «0%» — وهي قيمةٌ صحيحةُ الشكل تعني **عدم خضوع**، لا «لم يُجَب». والفرقُ بينهما هو الفرق
  // بين «معفى» و«ناقصُ إعداد»، وهو ما تحرسه ADR-0055 في القاعدة نفسها.
  if (percent === '' || percent === null || percent === undefined) return null;

  const value = Number(percent);
  if (!Number.isFinite(value)) return null;
  return Math.round(value * 100);
}

/** الإصداراتُ من الأحدث إلى الأقدم — ترتيبُ القراءة: ما يسري الآن أوّلاً. */
export const versionsNewestFirst = (profile) =>
  [...(profile?.versions ?? [])].sort((a, b) => b.version - a.version);

/**
 * الإصدارُ النافذ الآن: أحدثُ **منشورٍ** لا يتجاوز تاريخُ نفاذه اللحظة.
 *
 * والمسوّدةُ لا تنفذ على أحد ولو كان تاريخُها في الماضي — القاعدةُ نفسها في `TaxProfile.VersionOn`،
 * وتكرارُها هنا عرضٌ لا حساب: الخادم هو من يقرّر عند الاحتساب.
 * @param {{ versions?: Array<{ status: string, effectiveFrom: string, version: number }> }} profile
 * @param {Date | string} [at]
 */
export function effectiveVersion(profile, at = new Date()) {
  const instant = new Date(at).getTime();
  return versionsNewestFirst(profile)
    .filter((v) => v.status === 'Published' && new Date(v.effectiveFrom).getTime() <= instant)
    .sort((a, b) => (new Date(b.effectiveFrom).getTime() - new Date(a.effectiveFrom).getTime())
      || (b.version - a.version))[0] ?? null;
}

/** المسوّدةُ القائمة، إن وُجدت. واحدةٌ على الأكثر — يفرضه المجال. */
export const draftVersion = (profile) =>
  (profile?.versions ?? []).find((v) => v.status === 'Draft') ?? null;

/**
 * هل يُجمَع بهذا الملفّ أصلاً؟ **منشورٌ ومتحقَّقٌ منه معاً** — وهي البوّابة التي يوجد الملفّ
 * كلُّه لأجلها، مكرَّرةً هنا كي تُقرأ في القائمة قبل أن يختار متجرٌ ملفّاً لا يجمع شيئاً.
 */
export const profileCollects = (profile) =>
  (profile?.versions ?? []).some((v) => v.status === 'Published' && v.verificationState === 'Verified');

/**
 * سببُ عدم الجمع لمتجر، مشتقّاً من إعداده — بالرموز نفسها التي يرسلها الخادم، فلا مفردتان
 * لسؤالٍ واحد.
 * @param {{ taxProfileId?: number | null, collectionEnabled?: boolean }} settings
 * @param {Parameters<typeof effectiveVersion>[0] | null} profile
 */
export function collectionReason(settings, profile) {
  if (!settings?.taxProfileId) return 'NoProfileSelected';
  if (!settings.collectionEnabled) return 'CollectionDisabled';
  if (!effectiveVersion(profile)) return 'NoEffectiveVersion';
  return profileCollects(profile) ? 'Collecting' : 'VersionNotVerified';
}

/**
 * مشاكلُ نسبةٍ قبل الإرسال. الرمزُ والاسمُ مطلوبان، والنقاطُ داخل مداها.
 * @param {{ code?: string, name?: string, percent?: unknown }} rate
 */
export function rateProblems(rate) {
  const problems = {};
  if (!String(rate?.code ?? '').trim()) problems.code = 'required';
  if (!String(rate?.name ?? '').trim()) problems.name = 'required';
  const points = basisPointsOf(rate?.percent);
  if (points === null || points < 0 || points > MAX_BASIS_POINTS) problems.percent = 'range';
  return problems;
}

/**
 * مشاكلُ مسوّدةِ إصدارٍ قبل الإرسال.
 *
 * **و«ضريبةُ الشحن» بلا افتراض**: هي قاعدةُ اختصاصٍ لا اختيارٌ هندسيّ، ولا قيمةَ افتراضية لها
 * في المجال — فالنموذجُ يطلبها صراحةً، ويرفض «لم يُجَب». وافتراضُ أحد الجوابين خطأٌ بمقدار
 * ضريبةِ الشحن في كل طلب، في اتجاهٍ لا يظهر إلّا في تسويةٍ ضريبية.
 * @param {{ effectiveFrom?: string, priceMode?: string, shippingTaxable?: unknown,
 *           rates?: Array<object> }} version
 */
export function versionProblems(version) {
  const problems = {};
  if (!version?.effectiveFrom) problems.effectiveFrom = 'required';
  if (version?.priceMode !== 'Inclusive' && version?.priceMode !== 'Exclusive') problems.priceMode = 'required';
  if (version?.shippingTaxable !== true && version?.shippingTaxable !== false) problems.shippingTaxable = 'required';
  if (!version?.rates?.length) problems.rates = 'atLeastOne';
  return problems;
}

/** عُرفا السعر، كما يقبلهما الخادم. */
export const PRICE_MODES = ['Exclusive', 'Inclusive'];
