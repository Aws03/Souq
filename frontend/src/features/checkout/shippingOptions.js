// ============================================================================
// طريقة الشحن في الدفع (المرحلة 12) — منطق خالص مُختبَر: دولة العنوان المختار (عنوان الدفتر يحمل دولته؛ النصّي الحرّ لا)،
// الطريقة المختارة تبقى ما دامت متاحة للعنوان وإلا الأولى بترتيب المتجر، ونصّ المدّة التقديرية.
// ============================================================================
import { NEW_ADDRESS } from './shippingChoice';

export const countryOfChoice = (addresses, choice) =>
  (choice === NEW_ADDRESS ? null : addresses?.find((a) => a.id === choice)?.country ?? null);

export const pickShippingMethod = (options, current) =>
  (options?.some((o) => o.methodId === current) ? current : options?.[0]?.methodId ?? null);

// "1–3" أو "2" أيام — بلا مدّة ⇒ null.
export function estimateLabel(minDays, maxDays, t) {
  if (minDays == null || maxDays == null) return null;
  return minDays === maxDays
    ? t('checkout.shipping.daysExact', { days: maxDays })
    : t('checkout.shipping.daysRange', { min: minDays, max: maxDays });
}

// ما يمنع إرسال الطلب بسبب الشحن: متجر بطرق ولا طريقة تخدم العنوان، أو لم تُختر.
export function shippingProblem(shipping, methodId) {
  if (!shipping?.required) return null;
  if (shipping.options.length === 0) return 'unavailable';
  return methodId == null ? 'required' : null;
}
