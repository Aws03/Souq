// ============================================================================
// نموذج العنوان (المرحلة 7) — منطق خالص مُختبَر بـ Vitest: حقول فارغة أو من عنوان محفوظ، جسم الطلب كما يقبله الخادم
// (مقصوص، الدولة بحرفين كبيرين، الاختياري الفارغ null)، الحقول الناقصة، وسطر عرض مختصر. الخادم يتحقّق من الصيغ.
// ============================================================================
export const REQUIRED_ADDRESS_FIELDS = ['recipientName', 'phone', 'country', 'city', 'line1'];

export const emptyAddress = () => ({
  label: '', recipientName: '', phone: '', country: 'JO', city: '', region: '', line1: '', line2: '', postalCode: '',
});

export function addressToForm(address) {
  const form = emptyAddress();
  for (const key of Object.keys(form)) form[key] = address?.[key] ?? form[key] ?? '';
  return form;
}

export function formToAddress(form) {
  const clean = (value) => (value ?? '').trim();
  const optional = (value) => clean(value) || null;
  return {
    recipientName: clean(form.recipientName),
    phone: clean(form.phone),
    country: clean(form.country).toUpperCase(),
    city: clean(form.city),
    line1: clean(form.line1),
    region: optional(form.region),
    line2: optional(form.line2),
    postalCode: optional(form.postalCode),
    label: optional(form.label),
  };
}

export const missingAddressFields = (form) => REQUIRED_ADDRESS_FIELDS.filter((field) => !(form?.[field] ?? '').trim());

export const formatAddressLine = (address) =>
  [address?.line1, address?.line2, address?.city, address?.region, address?.country].filter(Boolean).join('، ');
