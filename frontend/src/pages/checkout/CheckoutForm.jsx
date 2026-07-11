import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import CardMockupFields from './CardMockupFields';
import styles from './Checkout.module.css';

// عمود النموذج (يمين الصفحة في RTL): عنوان الشحن + بطاقة الدفع التجريبية.
export default function CheckoutForm({ address, setAddress, card, setCard, errors, touched, setTouched, busy, onSubmit }) {
  return (
    <form className={styles.panel} onSubmit={onSubmit} noValidate>
      <h2 className={styles.panelTitle}>عنوان الشحن</h2>
      <FormField label="العنوان الكامل" error={touched.address && errors.address}>
        <textarea rows={3} value={address} className={inputClass(touched.address && errors.address)}
          onChange={(e) => setAddress(e.target.value)}
          onBlur={() => setTouched((t) => ({ ...t, address: true }))}
          placeholder="المدينة، الحي، الشارع، رقم المبنى…" />
      </FormField>

      <h2 className={styles.panelTitle}>بيانات الدفع</h2>
      <CardMockupFields card={card} setCard={setCard} errors={errors} touched={touched} setTouched={setTouched} />

      <Button type="submit" variant="saffron" size="lg" loading={busy} className={styles.submit}>
        ادفع الآن
      </Button>
    </form>
  );
}
