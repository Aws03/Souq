// ============================================================================
// الدفع في الإدارة (المرحلة 11) — منطق خالص مُختبَر: وضع مفتاح Stripe (تجريبي/حقيقي)، أول مشكلة تمنع حفظ حساب المتجر،
// جسم طلبه (السرّ الفارغ لا يُرسَل فيبقى المحفوظ)، وأول مشكلة تمنع الاسترداد. الخادم يعيد التحقّق من كل ذلك.
// ============================================================================

const KEY = /^(pk|sk|rk)_(test|live)_\S{5,}$/;

const modeOf = (key, prefixes) => {
  const match = KEY.exec((key ?? '').trim());
  return match && prefixes.includes(match[1]) ? match[2] : null;
};

export const publishableMode = (key) => modeOf(key, ['pk']);
export const secretMode = (key) => modeOf(key, ['sk', 'rk']);

export const accountToForm = (account) => ({ publishableKey: account?.publishableKey ?? '', secretKey: '', webhookSecret: '' });

export function accountFormProblem(form, hasAccount) {
  const publishable = publishableMode(form.publishableKey);
  if (!publishable) return 'publishableInvalid';
  const secret = form.secretKey.trim();
  if (!secret && !hasAccount) return 'secretRequired';
  if (secret && !secretMode(secret)) return 'secretInvalid';
  if (secret && secretMode(secret) !== publishable) return 'modeMismatch';
  const webhook = form.webhookSecret.trim();
  if (webhook && !/^whsec_\S{4,}$/.test(webhook)) return 'webhookInvalid';
  return null;
}

export const buildAccountPayload = (form) => ({
  publishableKey: form.publishableKey.trim(),
  ...(form.secretKey.trim() ? { secretKey: form.secretKey.trim() } : {}),
  ...(form.webhookSecret.trim() ? { webhookSecret: form.webhookSecret.trim() } : {}),
});

// خانات العملة الصغرى من Intl (ISO 4217) — مصدر واحد مع تنسيق الأسعار (app/tenantModel، المرحلة 15).
export { currencyDecimals } from '../../../app/tenantModel';

// مبلغ فارغ ⇒ كل المتبقّي. وإلا موجب، لا يتجاوز المتاح، وبخانات العملة.
export function refundProblem(amount, refundable, decimals) {
  if (amount === '' || amount == null) return refundable > 0 ? null : 'nothingToRefund';
  const value = Number(amount);
  if (!Number.isFinite(value) || value <= 0) return 'amountInvalid';
  if (value > refundable) return 'exceedsRefundable';
  const [, fraction = ''] = String(amount).trim().split('.');
  if (fraction.length > decimals) return 'tooManyDecimals';
  return null;
}

export const buildRefundPayload = (amount, reason) => ({
  ...(amount === '' || amount == null ? {} : { amount: Number(amount) }),
  ...(reason?.trim() ? { reason: reason.trim() } : {}),
});

// ألوان الشارات من صنفي Admin.module.css الموجودين.
