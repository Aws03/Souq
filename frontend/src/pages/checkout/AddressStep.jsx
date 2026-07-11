import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { CheckIcon } from '../../components/icons/Icons';
import styles from './Checkout.module.css';

// خطوة أولى من الدفع: عنوان الشحن + كوبون خصم اختياري. عند "متابعة للدفع"
// يُنشأ الطلب فعلياً على الخادم (يحجز المخزون وينشئ نيّة دفع لدى Stripe).
export default function AddressStep({
  address, setAddress, addressTouched, setAddressTouched, addressError,
  couponCode, setCouponCode, couponPreview, couponError, couponBusy, onApplyCoupon,
  busy, onSubmit,
}) {
  return (
    <form className={styles.panel} onSubmit={onSubmit} noValidate>
      <h2 className={styles.panelTitle}>عنوان الشحن</h2>
      <FormField label="العنوان الكامل" error={addressTouched && addressError}>
        <textarea rows={3} value={address} className={inputClass(addressTouched && addressError)}
          onChange={(e) => setAddress(e.target.value)}
          onBlur={() => setAddressTouched(true)}
          placeholder="المدينة، الحي، الشارع، رقم المبنى…" />
      </FormField>

      <h2 className={styles.panelTitle}>كوبون خصم (اختياري)</h2>
      <div className={styles.couponRow}>
        <input value={couponCode} dir="ltr" className={inputClass(!!couponError)}
          onChange={(e) => setCouponCode(e.target.value.toUpperCase())}
          placeholder="مثال: SAVE10" />
        <Button type="button" variant="ghost" loading={couponBusy} onClick={onApplyCoupon} disabled={!couponCode.trim()}>
          تطبيق
        </Button>
      </div>
      {couponError && <span className={styles.couponError}>{couponError}</span>}
      {couponPreview && (
        <div className={styles.couponApplied}>
          <CheckIcon size={15} /> تم تطبيق الكوبون — خصم {couponPreview.discountAmount.toFixed(3)} د.أ
        </div>
      )}

      <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>
        متابعة للدفع
      </Button>
    </form>
  );
}
