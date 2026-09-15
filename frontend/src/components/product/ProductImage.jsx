import { useState } from 'react';
import { CameraIcon } from '../icons/Icons';
import { getProductName } from './ProductBadges';
import styles from './ProductImage.module.css';

// صور رفعها الأدمن الفعلية تبدأ بـ /uploads/ أو برابط كامل. أي شيء غير ذلك
// (مثل مفاتيح البذر القديمة) ليس صورة حقيقية أصلاً.
export const isRealImage = (url) => !!url && (url.startsWith('/uploads/') || /^https?:\/\//.test(url));

// ============================================================================
// صورة منتج واحدة، ببديل رمادي أنيق (أيقونة كاميرا، بلا إيموجي) حين لا توجد صورة مرفوعة بعد،
// أو حين يفشل تحميل الصورة الموجودة (رابط معطوب).
//
// fit="contain" يعرض الصورة كاملة بلا قصّ (بطاقات الكتالوج)؛ الافتراضي cover.
//
// التحميل الكسول افتراضي إلّا حين priority: صورة فوق الطيّة محمّلة كسولاً تؤخّر أكبر عنصر
// مرئي (LCP) — وهو بالضبط ما يقيسه المتصفّح كـ"متى صارت الصفحة مفيدة". فصدارة الواجهة
// تُحمَّل بأولوية، وما تحتها كسولاً.
//
// والظهور تدريجي: صورة تقفز فجأةً في شبكة تجعل التمرير يبدو متقطّعاً. التدرّج على opacity
// وحده (طبقة المُركِّب) ويُلغى تماماً مع تفضيل تقليل الحركة.
// ============================================================================
export default function ProductImage({ product, className = '', fit = 'cover', priority = false }) {
  const [broken, setBroken] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const showImage = isRealImage(product.imageUrl) && !broken;

  if (showImage) {
    return (
      <img src={product.imageUrl} alt={getProductName(product)}
        loading={priority ? 'eager' : 'lazy'}
        // fetchPriority يرفع الصورة الأهمّ في طابور الشبكة قبل ما دونها.
        fetchPriority={priority ? 'high' : 'auto'}
        decoding="async"
        className={`${styles.photo} ${fit === 'contain' ? styles.contain : ''} ${loaded ? styles.loaded : styles.loading} ${className}`}
        onLoad={() => setLoaded(true)}
        onError={() => setBroken(true)} />
    );
  }

  return (
    <div className={`${styles.fallback} ${className}`} aria-hidden="true">
      <CameraIcon />
    </div>
  );
}
