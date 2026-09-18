import { useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { StarIcon } from '../icons/Icons';
import styles from './StarRating.module.css';

const STARS = [1, 2, 3, 4, 5];

// ============================================================================
// نجوم تقييم: عرضٌ فقط (متوسّط) أو اختيارٌ تفاعلي (نموذج التقييم).
//
// **الحالتان مختلفتان في شجرة الإتاحة، وكانتا واحدة** (M19):
//
//   • **العرض** كان خمسة أزرار معطّلة داخل `span` تحمل `aria-label` — والتسمية على عنصرٍ بلا دور
//     تُهمَل، فكان قارئ الشاشة يُعلن في كل بطاقة منتج وكل صفّ مراجعة خمسة أزرارٍ معطّلة واحداً
//     واحداً، ولا يقول القيمة أبداً. صار صورةً واحدة اسمها القيمة، ونجومها مخفيّة عن الشجرة.
//   • **الاختيار** كان `role="radiogroup"` وأبناؤه `<button>` بلا `role="radio"` ولا `aria-checked`:
//     مجموعةٌ لا يُعرف أيّ عنصر فيها مختار، ولا تعمل بالأسهم كما يَعِد دورها. صار كلٌّ منها
//     `radio` يُعلن حالته، بـ tabindex متجوّل وأسهمٍ تتبع اتجاه القراءة — نفس نمط `Tabs` (M13).
// ============================================================================
export default function StarRating({ value, onChange, size = 16 }) {
  const { t } = useTranslation();
  const groupRef = useRef(null);
  const chosen = Math.round(value) || 0;

  if (!onChange) {
    return (
      <span className={styles.stars} role="img" aria-label={t('product.starAria', { n: chosen })}>
        {STARS.map((n) => (
          <span key={n} className={`${styles.star} ${n <= chosen ? styles.filled : ''}`} aria-hidden="true">
            <StarIcon size={size} filled={n <= chosen} />
          </span>
        ))}
      </span>
    );
  }

  // الأسهم تتبع اتجاه القراءة: في RTL اليسار يتقدّم. يُقرأ من الاتجاه المحسوب لا من لغةٍ مفترضة.
  const onKeyDown = (e) => {
    if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(e.key)) return;
    e.preventDefault();
    const rtl = groupRef.current ? getComputedStyle(groupRef.current).direction === 'rtl' : false;
    if (e.key === 'Home') return onChange(1);
    if (e.key === 'End') return onChange(5);
    const forward = e.key === (rtl ? 'ArrowLeft' : 'ArrowRight');
    onChange(Math.min(5, Math.max(1, (chosen || (forward ? 0 : 6)) + (forward ? 1 : -1))));
  };

  return (
    <span ref={groupRef} className={styles.stars} role="radiogroup" aria-label={t('product.ratingAria')}>
      {STARS.map((n) => (
        <button
          key={n}
          type="button"
          role="radio"
          aria-checked={n === chosen}
          // tabindex متجوّل: نقطة دخولٍ واحدة للمجموعة كلّها بدل خمس محطّات Tab. وبلا اختيارٍ بعد
          // تكون النجمة الأولى هي المحطّة، كما يقتضي نمط ARIA لمجموعةٍ لا قيمة لها.
          tabIndex={n === (chosen || 1) ? 0 : -1}
          className={`${styles.star} ${n <= chosen ? styles.filled : ''}`}
          onClick={() => onChange(n)}
          onKeyDown={onKeyDown}
          aria-label={t('product.starAria', { n })}
        >
          <StarIcon size={size} filled={n <= chosen} />
        </button>
      ))}
    </span>
  );
}
