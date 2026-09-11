import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Button from '../common/Button';
import { ErrorBanner } from '../common/StateViews';
import { inputClass } from '../common/FormField';
import StarRating from '../product/StarRating';
import { submittedMessageKey } from '../../features/reviews/ratingSummary';
import styles from './ReviewForm.module.css';

// نموذج إضافة تقييم. الخادم هو الحكم الفعلي في الأحقّية (اشترى واستلم المنتج،
// ولم يقيّمه من قبل) — نعرض رسالة الخطأ التي يُعيدها كما هي، لا نخمّنها هنا.
// متجر بالإشراف (المرحلة 13) يعيد status=Pending: نخبر العميل أن تقييمه ينتظر المراجعة بدل أن يبحث عنه في القائمة.
export default function ReviewForm({ productId, onSubmitted }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const submit = async (e) => {
    e.preventDefault();
    if (rating === 0) return setError(t('reviews.ratingRequired'));
    if (!comment.trim()) return setError(t('reviews.commentRequired'));

    setBusy(true); setError(null);
    try {
      const created = await api.createReview(productId, { rating, comment: comment.trim() });
      toast.success(t(submittedMessageKey(created?.status)));
      setRating(0); setComment('');
      onSubmitted?.();
    } catch (err) { setError(err.message); }
    finally { setBusy(false); }
  };

  return (
    <form className={styles.form} onSubmit={submit}>
      <h3 className={styles.title}>{t('reviews.addTitle')}</h3>
      {error && <ErrorBanner message={error} />}
      <StarRating value={rating} onChange={setRating} size={22} />
      <textarea className={inputClass(false, styles.textarea)} rows={3} value={comment}
        onChange={(e) => setComment(e.target.value)} placeholder={t('reviews.commentPlaceholder')} />
      <Button type="submit" variant="primary" loading={busy}>{t('reviews.submit')}</Button>
    </form>
  );
}
