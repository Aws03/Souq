import { describe, it, expect } from 'vitest';
import {
  PASSWORD_MAX_LENGTH, changePasswordProblems, isChangePasswordValid, newPasswordProblem,
} from './passwordForm';

// ============================================================================
// قواعد كلمة المرور. الخادم هو الحارس (PasswordRules.StrongPassword)، وما يُختبر هنا أن الواجهة
// **تقول الشيء نفسه قبل الإرسال** — لا أضيق فتمنع كلمة يقبلها الخادم، ولا أوسع فتُرسل ما يرفضه.
// ============================================================================
describe('كلمة المرور الجديدة', () => {
  it('تقبل ما يقبله الخادم: ثمانية على الأقل، حروف وأرقام معاً', () => {
    expect(newPasswordProblem('passw0rd')).toBeNull();
    expect(newPasswordProblem('Checkout@12345')).toBeNull();
    // حروف غير لاتينية تُحتسب حروفاً كما يحتسبها char.IsLetter — لا تمييز للإنجليزية.
    expect(newPasswordProblem('كلمةسر123')).toBeNull();
    // الأرقام العربية-الهندية أرقام في Unicode، وchar.IsDigit يقبلها كذلك.
    expect(newPasswordProblem('كلمةسر١٢٣')).toBeNull();
  });

  it('ترفض ما يرفضه الخادم، كلَّ قاعدة برسالتها', () => {
    expect(newPasswordProblem('')).toBe('auth.passwordRequired');
    expect(newPasswordProblem('pass0')).toBe('auth.passwordMinLength');
    // ثمانية أحرف لكن بلا رقم — وهذه بالضبط ما كانت شاشة إعادة التعيين تمرّرها إلى الخادم.
    expect(newPasswordProblem('aaaaaaaa')).toBe('auth.passwordLettersAndDigits');
    // وبالعكس: أرقام بلا حرف.
    expect(newPasswordProblem('12345678')).toBe('auth.passwordLettersAndDigits');
    expect(newPasswordProblem(`a1${'x'.repeat(PASSWORD_MAX_LENGTH)}`)).toBe('auth.passwordMaxLength');
  });

  it('الطول يُقاس قبل التركيب: رسالة واحدة لا رسالتان', () => {
    // "a1" قصيرة *و*ناقصة لا شيء؛ الرسالة المفيدة هي الطول.
    expect(newPasswordProblem('a1')).toBe('auth.passwordMinLength');
  });
});

describe('نموذج تغيير كلمة المرور', () => {
  const form = { currentPassword: 'old-passw0rd', newPassword: 'new-passw0rd', confirm: 'new-passw0rd' };

  it('نموذج صحيح بلا مشاكل', () => {
    const problems = changePasswordProblems(form);
    expect(problems).toEqual({ currentPassword: null, newPassword: null, confirm: null });
    expect(isChangePasswordValid(problems)).toBe(true);
  });

  it('الحالية مطلوبة — الخادم يتحقّق منها، فطلبٌ بلا شيء هدرٌ لرحلة', () => {
    expect(changePasswordProblems({ ...form, currentPassword: '' }).currentPassword)
      .toBe('account.currentPasswordRequired');
  });

  it('التأكيد يجب أن يطابق، ومقارنته بالجديدة لا بالحالية', () => {
    expect(changePasswordProblems({ ...form, confirm: 'something-else' }).confirm)
      .toBe('auth.confirmPasswordMismatch');
    // حقلان فارغان متطابقان: المشكلة في الجديدة لا في التأكيد.
    const empty = changePasswordProblems({ currentPassword: 'old-passw0rd', newPassword: '', confirm: '' });
    expect(empty).toMatchObject({ newPassword: 'auth.passwordRequired', confirm: null });
  });

  it('الجديدة المساوية للحالية مرفوضة — كما يرفضها ChangePasswordValidator', () => {
    const same = changePasswordProblems({
      currentPassword: 'same-passw0rd', newPassword: 'same-passw0rd', confirm: 'same-passw0rd',
    });
    expect(same.newPassword).toBe('account.passwordSameAsCurrent');
    expect(isChangePasswordValid(same)).toBe(false);
  });

  it('"تساوي الحالية" تُقال بعد أن تصحّ الجديدة في نفسها', () => {
    // كلمة ضعيفة تساوي الحالية: الرسالة عن ضعفها، لا عن تساويها — وإلا صحّحها المستخدم بتغيير
    // تافه ثم قرأ رسالة ثانية عن الحقل نفسه.
    const weak = changePasswordProblems({ currentPassword: 'aaaa', newPassword: 'aaaa', confirm: 'aaaa' });
    expect(weak.newPassword).toBe('auth.passwordMinLength');
  });
});
