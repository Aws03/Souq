import { useState } from 'react';

// صور رفعها الأدمن الفعلية (مرحلة 3ب) تبدأ بـ /uploads/ أو برابط كامل. أي شيء
// غير ذلك (مثل مفاتيح البذر القديمة "headphones") ليس صورة حقيقية أصلاً.
export const isRealImage = (url) => !!url && (url.startsWith('/uploads/') || /^https?:\/\//.test(url));

// صورة منتج واحدة بديلة أنيقة (بلا إيموجي): حرف اسم المنتج الأول على تدرّج
// هوية سوق. تُستخدم حين لا توجد صورة مرفوعة بعد، أو حين يفشل تحميل الصورة
// الموجودة (رابط معطوب) — كلا الحالتين يجب ألا تكسر الواجهة.
export default function ProductImage({ product, className = '' }) {
  const [broken, setBroken] = useState(false);
  const showImage = isRealImage(product.imageUrl) && !broken;

  if (showImage) {
    return (
      <img src={product.imageUrl} alt={product.name} className={`product-photo ${className}`}
        onError={() => setBroken(true)} />
    );
  }

  const initial = (product.name || '؟').trim().charAt(0);
  return (
    <div className={`product-photo-fallback ${className}`} aria-hidden="true">
      <span>{initial}</span>
    </div>
  );
}
