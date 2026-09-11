// ============================================================================
// نموذج طريقة الشحن (المرحلة 12) — منطق خالص مُختبَر: حالة النموذج من طريقة، جسم الطلب كما يقبله الخادم (الدول رموز كبيرة
// بلا تكرار)، وأول مشكلة تمنع الحفظ (رمز ترجمة). الكيان على الخادم يعيد التحقّق من القواعد نفسها.
// ============================================================================

export const methodToForm = (method) => ({
  name: method?.name ?? '',
  price: method?.price ?? '',
  freeOverAmount: method?.freeOverAmount ?? '',
  minDays: method?.minDays ?? '',
  maxDays: method?.maxDays ?? '',
  carrier: method?.carrier ?? '',
  trackingUrlTemplate: method?.trackingUrlTemplate ?? '',
  countries: (method?.countries ?? []).join(', '),
  sortOrder: method?.sortOrder ?? 0,
  isActive: method?.isActive ?? true,
});

const numberOrNull = (value) => (value === '' || value == null ? null : Number(value));

export const parseCountries = (text) =>
  [...new Set((text ?? '').split(/[\s,،]+/).map((c) => c.trim().toUpperCase()).filter(Boolean))];

export const buildMethodPayload = (form) => ({
  name: form.name.trim(),
  price: Number(form.price),
  freeOverAmount: numberOrNull(form.freeOverAmount),
  minDays: numberOrNull(form.minDays),
  maxDays: numberOrNull(form.maxDays),
  carrier: form.carrier.trim() || null,
  trackingUrlTemplate: form.trackingUrlTemplate.trim() || null,
  countries: parseCountries(form.countries),
  sortOrder: Number(form.sortOrder) || 0,
  isActive: form.isActive,
});

export function methodFormProblem(form) {
  if (!form.name.trim()) return 'nameRequired';
  if (form.price === '' || !(Number(form.price) >= 0)) return 'priceInvalid';
  if (form.freeOverAmount !== '' && !(Number(form.freeOverAmount) > 0)) return 'freeOverInvalid';
  const min = numberOrNull(form.minDays);
  const max = numberOrNull(form.maxDays);
  if ((min == null) !== (max == null) || (min != null && (min < 0 || min > max))) return 'estimateInvalid';
  const template = form.trackingUrlTemplate.trim();
  if (template && (!template.startsWith('https://') || !template.includes('{number}'))) return 'trackingUrlInvalid';
  if (parseCountries(form.countries).some((c) => !/^[A-Z]{2}$/.test(c))) return 'countriesInvalid';
  return null;
}
