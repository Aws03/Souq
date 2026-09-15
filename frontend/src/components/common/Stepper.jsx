import { useTranslation } from 'react-i18next';
import { PlusIcon, MinusIcon } from '../icons/Icons';
import styles from './Stepper.module.css';

// مُنقّص/مُزيد كمية — السلّة وصفحة المنتج. min يمنع النزول تحت حدّ، وmax يوقف الزيادة عند المتاح
// (السلّة لا تمرّره: هناك يُعلَّم تجاوز المتاح برسالة بدل منعه، فيرى المشتري سبب توقّف الدفع).
export default function Stepper({ value, onInc, onDec, min = 1, max = Infinity }) {
  const { t } = useTranslation();
  return (
    <div className={styles.stepper}>
      <button type="button" onClick={onDec} disabled={value <= min} aria-label={t('common.decreaseQty')}>
        <MinusIcon />
      </button>
      <span className={styles.value}>{value}</span>
      <button type="button" onClick={onInc} disabled={value >= max} aria-label={t('common.increaseQty')}>
        <PlusIcon />
      </button>
    </div>
  );
}
