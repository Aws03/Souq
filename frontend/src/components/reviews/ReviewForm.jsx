import { useState } from 'react';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Button from '../common/Button';
import { ErrorBanner } from '../common/StateViews';
import { inputClass } from '../common/FormField';
import StarRating from '../product/StarRating';
import styles from './ReviewForm.module.css';

// نموذج إضافة تقييم. الخادم هو الحكم الفعلي في الأحقّية (اشترى واستلم المنتج،
// ولم يقيّمه من قبل) — نعرض رسالة الخطأ التي يُعيدها كما هي، لا نخمّنها هنا.
export default function ReviewForm({ productId, onSubmitted }) {
  const toast = useToast();
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const submit = async (e) => {
    e.preventDefault();
    if (rating === 0) return setError('اختر تقييماً من 1 إلى 5 نجوم');
    if (!comment.trim()) return setError('اكتب تعليقاً عن تجربتك مع المنتج');

    setBusy(true); setError(null);
    try {
      await api.createReview(productId, { rating, comment: comment.trim() });
      toast.success('تم إرسال تقييمك — شكراً لك');
      setRating(0); setComment('');
      onSubmitted?.();
    } catch (err) { setError(err.message); }
    finally { setBusy(false); }
  };

  return (
    <form className={styles.form} onSubmit={submit}>
      <h3 className={styles.title}>أضف تقييمك</h3>
      {error && <ErrorBanner message={error} />}
      <StarRating value={rating} onChange={setRating} size={22} />
      <textarea className={inputClass(false, styles.textarea)} rows={3} value={comment}
        onChange={(e) => setComment(e.target.value)} placeholder="شاركنا رأيك في المنتج…" />
      <Button type="submit" variant="primary" loading={busy}>إرسال التقييم</Button>
    </form>
  );
}
