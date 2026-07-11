import { useTranslation } from 'react-i18next';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { CheckIcon } from '../../components/icons/Icons';
import { formatPrice } from '../../components/product/ProductBadges';
import styles from './Checkout.module.css';

// خطوة أولى من الدفع: عنوان الشحن + كوبون خصم اختياري. عند "متابعة للدفع"
// يُنشأ الطلب فعلياً على الخادم (يحجز المخزون وينشئ نيّة دفع لدى Stripe).
export default function AddressStep({
  address, setAddress, addressTouched, setAddressTouched, addressError,
  couponCode, setCouponCode, couponPreview, couponError, couponBusy, onApplyCoupon,
  busy, onSubmit,
}) {
  const { t } = useTranslation();
  return (
    <form className={styles.panel} onSubmit={onSubmit} noValidate>
      <h2 className={styles.panelTitle}>{t('checkout.shippingAddressTitle')}</h2>
      <FormField label={t('checkout.addressLabel')} error={addressTouched && addressError}>
        <textarea rows={3} value={address} className={inputClass(addressTouched && addressError)}
          onChange={(e) => setAddress(e.target.value)}
          onBlur={() => setAddressTouched(true)}
          placeholder={t('checkout.addressPlaceholder')} />
      </FormField>

      <h2 className={styles.panelTitle}>{t('checkout.couponTitle')}</h2>
      <div className={styles.couponRow}>
        <input value={couponCode} dir="ltr" className={inputClass(!!couponError)}
          onChange={(e) => setCouponCode(e.target.value.toUpperCase())}
          placeholder={t('checkout.couponPlaceholder')} />
        <Button type="button" variant="ghost" loading={couponBusy} onClick={onApplyCoupon} disabled={!couponCode.trim()}>
          {t('checkout.applyCoupon')}
        </Button>
      </div>
      {couponError && <span className={styles.couponError}>{couponError}</span>}
      {couponPreview && (
        <div className={styles.couponApplied}>
          <CheckIcon size={15} /> {t('checkout.couponApplied', { amount: formatPrice(couponPreview.discountAmount, 'JOD') })}
        </div>
      )}

      <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>
        {t('checkout.continueToPayment')}
      </Button>
    </form>
  );
}
