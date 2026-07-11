import { useState } from 'react';
import { CameraIcon } from '../icons/Icons';
import styles from './ProductImage.module.css';

// صور رفعها الأدمن الفعلية تبدأ بـ /uploads/ أو برابط كامل. أي شيء غير ذلك
// (مثل مفاتيح البذر القديمة) ليس صورة حقيقية أصلاً.
export const isRealImage = (url) => !!url && (url.startsWith('/uploads/') || /^https?:\/\//.test(url));

// صورة منتج واحدة، ببديل رمادي أنيق (أيقونة كاميرا، بلا إيموجي) حين لا توجد
// صورة مرفوعة بعد، أو حين يفشل تحميل الصورة الموجودة (رابط معطوب).
export default function ProductImage({ product, className = '' }) {
  const [broken, setBroken] = useState(false);
  const showImage = isRealImage(product.imageUrl) && !broken;

  if (showImage) {
    return (
      <img src={product.imageUrl} alt={product.name} className={`${styles.photo} ${className}`}
        onError={() => setBroken(true)} />
    );
  }

  return (
    <div className={`${styles.fallback} ${className}`} aria-hidden="true">
      <CameraIcon />
    </div>
  );
}
