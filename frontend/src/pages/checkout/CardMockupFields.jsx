import { CardIcon } from '../../components/icons/Icons';
import FormField, { inputClass } from '../../components/common/FormField';
import styles from './CardMockupFields.module.css';

const formatCardNumber = (raw) => raw.replace(/\D/g, '').slice(0, 16).replace(/(.{4})/g, '$1 ').trim();
const formatExpiry = (raw) => {
  const digits = raw.replace(/\D/g, '').slice(0, 4);
  return digits.length > 2 ? `${digits.slice(0, 2)}/${digits.slice(2)}` : digits;
};

/** واجهة إدخال بطاقة دفع تجريبية (Mockup): معاينة مرئية + حقول رقم/انتهاء/CVV. */
export default function CardMockupFields({ card, setCard, errors, touched, setTouched }) {
  const set = (key, formatter) => (e) => setCard((c) => ({ ...c, [key]: formatter(e.target.value) }));
  const blur = (key) => () => setTouched((t) => ({ ...t, [key]: true }));

  return (
    <div>
      <div className={styles.preview}>
        <CardIcon size={26} />
        <div className={styles.previewNumber}>{card.number || '•••• •••• •••• ••••'}</div>
        <div className={styles.previewRow}>
          <span>{card.expiry || 'MM/YY'}</span>
        </div>
      </div>

      <FormField label="رقم البطاقة" error={touched.number && errors.number}>
        <input dir="ltr" inputMode="numeric" value={card.number} className={inputClass(touched.number && errors.number)}
          onChange={set('number', formatCardNumber)} onBlur={blur('number')} placeholder="0000 0000 0000 0000" />
      </FormField>

      <div className={styles.row}>
        <FormField label="تاريخ الانتهاء" error={touched.expiry && errors.expiry}>
          <input dir="ltr" inputMode="numeric" value={card.expiry} className={inputClass(touched.expiry && errors.expiry)}
            onChange={set('expiry', formatExpiry)} onBlur={blur('expiry')} placeholder="MM/YY" />
        </FormField>
        <FormField label="CVV" error={touched.cvv && errors.cvv}
          hint={!touched.cvv || !errors.cvv ? 'للاختبار: 000 يحاكي رفض الدفع' : undefined}>
          <input dir="ltr" inputMode="numeric" value={card.cvv}
            className={inputClass(touched.cvv && errors.cvv)}
            onChange={set('cvv', (v) => v.replace(/\D/g, '').slice(0, 3))}
            onBlur={blur('cvv')} placeholder="123" />
        </FormField>
      </div>
    </div>
  );
}
