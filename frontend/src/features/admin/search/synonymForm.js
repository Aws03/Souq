// ============================================================================
// نموذج مفردة البحث (M3، ADR-0042) — منطق خالص مُختبَر.
//
// الخادم هو صاحب القرار: كلمة واحدة لكل طرف، لغة مدعومة، ولا كلمة إلى نفسها بعد التطبيع. ما هنا يسبق أخطاءه
// المعروفة كي لا يدفع التاجر ثمن ذهاب وإياب على خطأ واضح — لا ليحلّ محلّه. التطبيع نفسه **لا يُكرَّر هنا**:
// تنفيذه في المجال، ونسخة ثانية منه في JavaScript كانت ستفترق عنه بصمت وتقبل ما يرفضه أو العكس.
// ============================================================================

/** @typedef {{culture: string, term: string, expansion: string}} SynonymForm */

/**
 * @param {{culture?: string, term?: string, expansion?: string}|null} [synonym]
 * @returns {SynonymForm}
 */
export function synonymToForm(synonym) {
  return {
    culture: synonym?.culture || 'ar',
    term: synonym?.term || '',
    expansion: synonym?.expansion || '',
  };
}

/**
 * مفتاح الخطأ إن وُجد، وإلا null. يُترجَم تحت admin.searchSynonyms.form.*
 * @param {SynonymForm} form
 * @returns {string|null}
 */
export function synonymFormProblem(form) {
  const term = (form.term || '').trim();
  const expansion = (form.expansion || '').trim();

  if (!term || !expansion) return 'bothRequired';
  // كلمة واحدة لكل طرف: يُقاس على المسافات وحدها — أمّا التساوي بعد التطبيع فيقرّره الخادم.
  if (term.split(/\s+/).length > 1 || expansion.split(/\s+/).length > 1) return 'singleWord';
  return null;
}

/**
 * @param {SynonymForm} form
 * @returns {{culture: string, term: string, expansion: string}}
 */
export function buildSynonymPayload(form) {
  return {
    culture: form.culture,
    term: (form.term || '').trim(),
    expansion: (form.expansion || '').trim(),
  };
}
