import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import { CheckIcon } from '../../components/icons/Icons';
import { formatPrice } from '../../components/product/ProductBadges';
import { useModule } from '../../app/TenantProvider';
import { formatAddressLine } from '../../features/account/addressForm';
import { NEW_ADDRESS } from '../../features/checkout/shippingChoice';
import { estimateLabel } from '../../features/checkout/shippingOptions';
import styles from './Checkout.module.css';

// خطوة أولى من الدفع: عنوان الشحن + طريقة الشحن (المرحلة 12) + كوبون خصم اختياري. عند "متابعة للدفع" يُنشأ الطلب فعلياً
// على الخادم. عنوان من الدفتر يُرسَل بمعرّفه فقط (المرحلة 7)، أو عنوان نصّي آخر. الطرق المعروضة ما يخدم دولة العنوان
// المختار كما يسعّرها الخادم — والعنوان النصّي بلا دولة لا يرى إلا الطرق غير المقيَّدة بدول.
export default function AddressStep({
  savedAddresses, shippingChoice, setShippingChoice,
  address, setAddress, addressTouched, setAddressTouched, addressError,
  shipping, shippingMethodId, setShippingMethodId, shippingIssue, currency,
  couponCode, setCouponCode, couponPreview, couponError, couponBusy, onApplyCoupon,
  busy, blocked, onSubmit,
}) {
  const { t } = useTranslation();
  const couponsEnabled = useModule('promotions');   // وحدة الكوبونات (المرحلة 15): معطّلة ⇒ لا حقل كوبون
  const choiceClass = (selected) => `${styles.addressChoice} ${selected ? styles.addressChoiceActive : ''}`;

  return (
    <form className={styles.panel} onSubmit={onSubmit} noValidate>
      <h2 className={styles.panelTitle}>{t('checkout.shippingAddressTitle')}</h2>

      {savedAddresses === null ? <Skeleton height={96} radius={12} /> : (
        <>
          {savedAddresses.length > 0 && (
            <div className={styles.addressChoices} role="radiogroup" aria-label={t('checkout.savedAddressesLabel')}>
              {savedAddresses.map((a) => (
                <label key={a.id} className={choiceClass(shippingChoice === a.id)}>
                  <input type="radio" name="shipping-address" checked={shippingChoice === a.id}
                    onChange={() => setShippingChoice(a.id)} />
                  <span className={styles.addressChoiceText}>
                    <b>{a.label || a.recipientName}</b>
                    <span className={styles.addressChoiceLine}>{formatAddressLine(a)}</span>
                  </span>
                </label>
              ))}
              <label className={choiceClass(shippingChoice === NEW_ADDRESS)}>
                <input type="radio" name="shipping-address" checked={shippingChoice === NEW_ADDRESS}
                  onChange={() => setShippingChoice(NEW_ADDRESS)} />
                <span className={styles.addressChoiceText}><b>{t('checkout.useNewAddress')}</b></span>
              </label>
              <Link to="/account" className={styles.manageAddresses}>{t('checkout.manageAddresses')}</Link>
            </div>
          )}

          {shippingChoice === NEW_ADDRESS && (
            <FormField label={t('checkout.addressLabel')} error={addressTouched && addressError}>
              <textarea rows={3} value={address} className={inputClass(addressTouched && addressError)}
                onChange={(e) => setAddress(e.target.value)}
                onBlur={() => setAddressTouched(true)}
                placeholder={t('checkout.addressPlaceholder')} />
            </FormField>
          )}
        </>
      )}

      {shipping?.required && (
        <>
          <h2 className={styles.panelTitle}>{t('checkout.shipping.title')}</h2>
          {shipping.options.length === 0 ? (
            <p className={styles.blockedNote}>
              {shippingChoice === NEW_ADDRESS ? t('checkout.shipping.needsSavedAddress') : t('checkout.shipping.unavailable')}
            </p>
          ) : (
            <div className={styles.addressChoices} role="radiogroup" aria-label={t('checkout.shipping.title')}>
              {shipping.options.map((o) => {
                const estimate = estimateLabel(o.minDays, o.maxDays, t);
                return (
                  <label key={o.methodId} className={choiceClass(shippingMethodId === o.methodId)}>
                    <input type="radio" name="shipping-method" checked={shippingMethodId === o.methodId}
                      onChange={() => setShippingMethodId(o.methodId)} />
                    <span className={styles.addressChoiceText}>
                      <b>{o.name} · {o.cost > 0 ? formatPrice(o.cost, currency) : t('cart.free')}</b>
                      {estimate && <span className={styles.addressChoiceLine}>{estimate}</span>}
                    </span>
                  </label>
                );
              })}
            </div>
          )}
        </>
      )}

      {couponsEnabled && (
        <>
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
              <CheckIcon size={15} /> {t('checkout.couponApplied', { amount: formatPrice(couponPreview.discountAmount, currency) })}
            </div>
          )}
        </>
      )}

      {blocked && <p className={styles.blockedNote}>{t('cart.fixItems')}</p>}
      <Button type="submit" variant="saffron" size="lg" loading={busy}
        disabled={savedAddresses === null || blocked || shippingIssue === 'unavailable'} className={styles.submit}>
        {t('checkout.continueToPayment')}
      </Button>
    </form>
  );
}
