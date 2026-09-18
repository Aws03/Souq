import { useTranslation } from 'react-i18next';
import ProductImage from '../product/ProductImage';
import { formatPrice, getProductName } from '../product/ProductBadges';
import { lineProblem } from '../../features/basket/basketModel';
import Stepper from '../common/Stepper';
import { TrashIcon } from '../icons/Icons';
import styles from './CartLine.module.css';

// سطر واحد داخل درج السلة: صورة مصغّرة + اسم + سعر الوحدة + منقّص/مُزيد + إجمالي السطر كما حسبه الخادم. سطر لم يعد
// متاحاً أو يتجاوز المتاح يُعلَّم بسببه (الدفع موقوف حتى يُعدَّل).
export default function CartLine({ item, onInc, onDec, onRemove }) {
  const { t } = useTranslation();
  const name = getProductName(item);
  const problem = lineProblem(item);

  return (
    <div className={`${styles.line} ${problem ? styles.problem : ''}`}>
      <div className={styles.thumb}><ProductImage product={item} /></div>
      <div className={styles.info}>
        <div className={styles.name}>{name}</div>
        {/* وصف المتغيّر الحيّ من الخادم ("M / أحمر")، واتجاهه من محتواه: نصّ التاجر قد يمزج العربية واللاتينية. */}
        {item.variantLabel && <div className={styles.variant} dir="auto">{item.variantLabel}</div>}
        <div className={styles.unit}>{formatPrice(item.price, item.currency)} {t('cart.perUnit')}</div>
        {problem && <div className={styles.warning}>{t(`cart.${problem.code}`, { count: problem.count })}</div>}
      </div>
      {/* العدّاد والإجمالي في غلاف واحد: على الشاشات الضيّقة يهبطان معاً إلى سطر ثانٍ تحت الاسم بدل أن
          تُضغط خمسة أعمدة في 320px فيخرج زرّ الحذف خارج النافذة (قيس في مسح M4). */}
      <div className={styles.controls}>
        <Stepper value={item.qty} onInc={() => onInc(item.variantId)} onDec={() => onDec(item.variantId)} />
        <div className={styles.lineTotal}>{item.sellable ? formatPrice(item.lineTotal, item.currency) : '—'}</div>
      </div>
      <button type="button" className={styles.remove} onClick={() => onRemove(item.variantId)}
        aria-label={t('cart.removeAria', { name: item.variantLabel ? `${name} (${item.variantLabel})` : name })}>
        <TrashIcon />
      </button>
    </div>
  );
}
