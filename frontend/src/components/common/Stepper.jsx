import { PlusIcon, MinusIcon } from '../icons/Icons';
import styles from './Stepper.module.css';

// مُنقّص/مُزيد كمية — يُستخدم في سلّة المشتريات. min يمنع النزول تحت حدّ معيّن.
export default function Stepper({ value, onInc, onDec, min = 1 }) {
  return (
    <div className={styles.stepper}>
      <button type="button" onClick={onDec} disabled={value <= min} aria-label="إنقاص الكمية">
        <MinusIcon />
      </button>
      <span className={styles.value}>{value}</span>
      <button type="button" onClick={onInc} aria-label="زيادة الكمية">
        <PlusIcon />
      </button>
    </div>
  );
}
