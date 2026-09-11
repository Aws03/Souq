// ============================================================================
// نموذج الكوبون (المرحلة 10) — منطق خالص مُختبَر: حالة النموذج من كوبون، جسم الطلب كما يقبله الخادم، وأول مشكلة تمنع
// الحفظ (رمز ترجمة). الخادم والكيان يعيدان التحقّق من القواعد نفسها.
// ============================================================================

// تاريخ HTML (yyyy-MM-dd) ⇄ ISO — تحويل بسيط ذهاباً وإياباً لحقل <input type="date">.
export const toDateInput = (iso) => (iso ? iso.slice(0, 10) : '');

export const couponToForm = (coupon) => ({
  code: coupon?.code ?? '',
  type: coupon?.type ?? 'Percentage',
  value: coupon?.value ?? '',
  minOrderAmount: coupon?.minOrderAmount ?? '',
  startsAt: toDateInput(coupon?.startsAt),
  expiresAt: toDateInput(coupon?.expiresAt),
  maxUses: coupon?.maxUses ?? '',
  maxUsesPerCustomer: coupon?.maxUsesPerCustomer ?? '',
  isActive: coupon?.isActive ?? true,
});

const numberOrNull = (value) => (value === '' || value == null ? null : Number(value));
const isoOrNull = (date) => (date ? new Date(date).toISOString() : null);

export const buildCouponPayload = (form, isEdit) => ({
  ...(isEdit ? {} : { code: form.code.trim().toUpperCase() }),
  type: form.type,
  value: Number(form.value),
  minOrderAmount: numberOrNull(form.minOrderAmount),
  startsAt: isoOrNull(form.startsAt),
  expiresAt: isoOrNull(form.expiresAt),
  maxUses: numberOrNull(form.maxUses),
  maxUsesPerCustomer: numberOrNull(form.maxUsesPerCustomer),
  isActive: form.isActive,
});

export function couponFormProblem(form, isEdit) {
  if (!isEdit && !form.code.trim()) return 'codeRequired';
  if (!form.value || Number(form.value) <= 0) return 'valueInvalid';
  if (form.startsAt && form.expiresAt && form.startsAt >= form.expiresAt) return 'windowInvalid';
  if (form.maxUses && form.maxUsesPerCustomer && Number(form.maxUsesPerCustomer) > Number(form.maxUses)) return 'perCustomerTooHigh';
  return null;
}
