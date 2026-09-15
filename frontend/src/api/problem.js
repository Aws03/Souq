// ============================================================================
// تحويل استجابة خطأ من الـ API إلى Error واحد تفهمه كل الشاشات (ADR-0017).
// الخادم يُرجع RFC 7807 ProblemDetails: { title, status, detail, code, traceId, errors }.
//   code    — العقد الثابت: تُبنى عليه الترجمة والمنطق، لا على نص الرسالة.
//   detail  — رسالة الخادم التفصيلية (بالعربية حالياً).
//   traceId — يُعرض/يُنسخ عند الدعم الفني ليُطابَق مع سجلات الخادم.
// اختيار الرسالة: إن كانت لغة الواجهة هي لغة رسائل الخادم نعرض detail (أدقّ: "انتهت
// صلاحية الكوبون" لا "كوبون غير صالح")، وإلا نترجم code — فلا يرى مستخدم الواجهة
// الإنجليزية نصاً عربياً من الخادم لأي رمز معروف (Phase 0 A9).
// دالة نقية بلا i18n ولا fetch كي تُختبر وحدها.
// ============================================================================
/**
 * جسم RFC 7807 كما يرسله الخادم (ADR-0017).
 * @typedef {{title?: string, status?: number, detail?: string, code?: string,
 *            traceId?: string, errors?: Record<string, string[]>}} ProblemDetails
 */

/**
 * خطأ الـ API كما تقرؤه كل شاشة — الشكل موصوف هنا لأنه عقد، لا تفصيل داخلي.
 * @typedef {Error & {status: number, code: string|null, traceId: string|null,
 *                   fieldErrors: Record<string, string[]>|null}} ApiError
 */

/**
 * @param {number} status
 * @param {unknown} body
 * @param {{translate?: (code: string) => string|null, preferServerDetail?: boolean,
 *          fallbackMessage?: string}} [options]
 * @returns {ApiError}
 */
export function toApiError(status, body, { translate = () => null, preferServerDetail = true, fallbackMessage = '' } = {}) {
  const problem = /** @type {ProblemDetails} */ (body && typeof body === 'object' ? body : {});
  const code = nonEmpty(problem.code);
  const fieldErrors = problem.errors && typeof problem.errors === 'object' ? problem.errors : null;
  const firstFieldError = fieldErrors ? nonEmpty(Object.values(fieldErrors).flat()[0]) : null;
  const serverMessage = nonEmpty(problem.detail) ?? firstFieldError;
  const translated = code ? nonEmpty(translate(code)) : null;

  const message = (preferServerDetail ? serverMessage ?? translated : translated ?? serverMessage) ?? fallbackMessage;

  const error = /** @type {ApiError} */ (new Error(message));
  error.status = status;
  error.code = code;
  error.traceId = nonEmpty(problem.traceId);
  error.fieldErrors = fieldErrors;
  return error;
}

function nonEmpty(value) {
  return typeof value === 'string' && value.trim() ? value : null;
}
