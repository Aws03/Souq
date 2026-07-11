import { useTranslation } from 'react-i18next';
import { PlusIcon, MinusIcon } from '../icons/Icons';
import styles from './Stepper.module.css';

// مُنقّص/مُزيد كمية — يُستخدم في سلّة المشتريات. min يمنع النزول تحت حدّ معيّن.
export default function Stepper({ value, onInc, onDec, min = 1 }) {
  const { t } = useTranslation();
  return (
    <div className={styles.stepper}>
      <button type="button" onClick={onDec} disabled={value <= min} aria-label={t('common.decreaseQty')}>
        <MinusIcon />
      </button>
      <span className={styles.value}>{value}</span>
      <button type="button" onClick={onInc} aria-label={t('common.increaseQty')}>
        <PlusIcon />
      </button>
    </div>
  );
}
