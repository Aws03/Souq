// ============================================================================
// اختيار عنوان الشحن في الدفع (المرحلة 7) — منطق خالص مُختبَر: عنوان من دفتر العميل يُرسَل بمعرّفه فقط (الخادم يأخذ
// لقطته من دفتر العميل الحالي نفسه، فمعرّف عنوان غيره يُرفض) أو عنوان نصّي حرّ. الافتراضي للشحن مختار مبدئياً.
// ============================================================================
export const NEW_ADDRESS = 'new';

export const initialShippingChoice = (addresses) =>
  addresses?.find((a) => a.isDefaultShipping)?.id ?? addresses?.[0]?.id ?? NEW_ADDRESS;

export const shippingPayload = (choice, freeText) =>
  choice === NEW_ADDRESS
    ? { shippingAddress: (freeText ?? '').trim(), shippingAddressId: null }
    : { shippingAddress: null, shippingAddressId: Number(choice) };

export const isShippingChoiceMissing = (choice, freeText) => choice === NEW_ADDRESS && !(freeText ?? '').trim();
