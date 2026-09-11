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
export function toApiError(status, body, { translate = () => null, preferServerDetail = true, fallbackMessage = '' } = {}) {
  const problem = body && typeof body === 'object' ? body : {};
  const code = nonEmpty(problem.code);
  const fieldErrors = problem.errors && typeof problem.errors === 'object' ? problem.errors : null;
  const firstFieldError = fieldErrors ? nonEmpty(Object.values(fieldErrors).flat()[0]) : null;
  const serverMessage = nonEmpty(problem.detail) ?? firstFieldError;
  const translated = code ? nonEmpty(translate(code)) : null;

  const message = (preferServerDetail ? serverMessage ?? translated : translated ?? serverMessage) ?? fallbackMessage;

  const error = new Error(message);
  error.status = status;
  error.code = code;
  error.traceId = nonEmpty(problem.traceId);
  error.fieldErrors = fieldErrors;
  return error;
}

function nonEmpty(value) {
  return typeof value === 'string' && value.trim() ? value : null;
}
