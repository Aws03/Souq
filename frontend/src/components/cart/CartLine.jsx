import { useTranslation } from 'react-i18next';
import ProductImage from '../product/ProductImage';
import { formatPrice } from '../product/ProductBadges';
import Stepper from '../common/Stepper';
import { TrashIcon } from '../icons/Icons';
import styles from './CartLine.module.css';

// سطر واحد داخل درج السلة: صورة مصغّرة + اسم + سعر إجمالي السطر + منقّص/مُزيد.
export default function CartLine({ item, onInc, onDec, onRemove }) {
  const { t } = useTranslation();
  return (
    <div className={styles.line}>
      <div className={styles.thumb}><ProductImage product={item} /></div>
      <div className={styles.info}>
        <div className={styles.name}>{item.name}</div>
        <div className={styles.unit}>{formatPrice(item.price, item.currency)} {t('cart.perUnit')}</div>
      </div>
      <Stepper value={item.qty} onInc={() => onInc(item.id)} onDec={() => onDec(item.id)} />
      <div className={styles.lineTotal}>{formatPrice(item.price * item.qty, item.currency)}</div>
      <button type="button" className={styles.remove} onClick={() => onRemove(item.id)} aria-label={t('cart.removeAria', { name: item.name })}>
        <TrashIcon />
      </button>
    </div>
  );
}
