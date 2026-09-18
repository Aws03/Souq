// ============================================================================
// قواعد كلمة المرور كما يفرضها الخادم، في وحدة واحدة (M9).
//
// **لماذا وحدة مشتركة لا تحقّق في كل شاشة؟** لأن الشاشات ثلاث (إعادة التعيين، قبول الدعوة، تغيير
// الكلمة) والقواعد واحدة على الخادم — `PasswordRules.StrongPassword`: ثمانية أحرف على الأقل، ومئة
// وثمانية وعشرون على الأكثر، وحروفٌ وأرقامٌ **معاً**. وشاشة إعادة التعيين كانت تفحص الطول وحده،
// فكلمةٌ مثل "aaaaaaaa" تمرّ منها ثم يرفضها الخادم: رحلةٌ كاملة لتُقرأ رسالةٌ كان يمكن أن تُقرأ فوراً.
//
// النسخة الواحدة لا تمنع الانحراف عن الخادم بنفسها — لا شيء يربط هذا الملفّ بـ PasswordRules.cs —
// لكنها تجعل الانحراف **موضعاً واحداً** يُصحَّح، بدل ثلاثة تنحرف كلٌّ في اتجاه.
//
// تعيد مفاتيح ترجمة لا نصوصاً: المنطق نقيّ، والعرض يترجم.
// ============================================================================
export const PASSWORD_MIN_LENGTH = 8;
export const PASSWORD_MAX_LENGTH = 128;

/**
 * مشكلة كلمة المرور الجديدة، أو null إن كانت مقبولة.
 * @param {string} value
 * @returns {string | null} مفتاح ترجمة
 */
export function newPasswordProblem(value) {
  const password = value ?? '';
  if (!password) return 'auth.passwordRequired';
  if (password.length < PASSWORD_MIN_LENGTH) return 'auth.passwordMinLength';
  if (password.length > PASSWORD_MAX_LENGTH) return 'auth.passwordMaxLength';
  // نفس شرط الخادم بالضبط: حرفٌ واحد ورقمٌ واحد يكفيان — لا رموز مطلوبة ولا حالة أحرف.
  // `\p{L}` لا `[a-z]`: كلمة مرور بحروف عربية أو غيرها تُحتسب حروفاً كما يحتسبها char.IsLetter.
  if (!/\p{L}/u.test(password) || !/\p{Nd}/u.test(password)) return 'auth.passwordLettersAndDigits';
  return null;
}

/**
 * مشاكل نموذج تغيير كلمة المرور كاملاً. الحقول الغائبة لا مشكلة لها (null).
 * @param {{ currentPassword?: string, newPassword?: string, confirm?: string }} form
 * @returns {{ currentPassword: string | null, newPassword: string | null, confirm: string | null }}
 */
export function changePasswordProblems(form) {
  const current = form.currentPassword ?? '';
  const next = form.newPassword ?? '';
  const confirm = form.confirm ?? '';

  const newPassword = newPasswordProblem(next)
    // الخادم يرفض الجديدة إن ساوت الحالية (ChangePasswordValidator). يُقال هنا قبل الإرسال، لكن
    // الفحص يؤجَّل حتى تصحّ الجديدة في نفسها كي لا تظهر رسالتان عن حقل واحد.
    ?? (next && next === current ? 'account.passwordSameAsCurrent' : null);

  return {
    currentPassword: current ? null : 'account.currentPasswordRequired',
    newPassword,
    confirm: confirm === next ? null : 'auth.confirmPasswordMismatch',
  };
}

/** @param {{ currentPassword: string | null, newPassword: string | null, confirm: string | null }} problems */
export const isChangePasswordValid = (problems) => Object.values(problems).every((p) => p === null);
