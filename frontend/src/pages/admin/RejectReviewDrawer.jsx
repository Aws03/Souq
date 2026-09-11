import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import StarRating from '../../components/product/StarRating';
import { NOTE_MAX_LENGTH } from '../../features/admin/reviews/reviewModeration';
import styles from './RejectReviewDrawer.module.css';

// رفض تقييم (المرحلة 13): يختفي من المتجر ويخرج من الإجماليات، والملاحظة للإدارة وحدها — لا تُعرض للعميل. onReject يستدعي
// api.rejectReview(review.id, note).
export default function RejectReviewDrawer({ review, onReject, onClose }) {
  const { t } = useTranslation();
  const [note, setNote] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true); setError(null);
    try { await onReject(note.trim() || null); }
    catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={t('admin.reviews.rejectTitle')}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="reject-review-form" loading={busy}>{t('admin.reviews.confirmReject')}</Button>
        </div>
      }>
      <form id="reject-review-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}
        <figure className={styles.quote}>
          <StarRating value={review.rating} size={14} />
          <blockquote>{review.comment}</blockquote>
          <figcaption>{review.customerName} · {review.productName}</figcaption>
        </figure>
        <FormField label={t('admin.reviews.noteLabel')} hint={t('admin.reviews.noteHint', { max: NOTE_MAX_LENGTH })}>
          <textarea className={inputClass(false)} rows={3} maxLength={NOTE_MAX_LENGTH} value={note}
            onChange={(e) => setNote(e.target.value)} />
        </FormField>
      </form>
    </Drawer>
  );
}
