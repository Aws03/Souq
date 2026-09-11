import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import StarRating from '../../components/product/StarRating';
import { formatDate } from '../../i18n';
import { REVIEW_STATUSES, STATUS_BADGE, buildReviewQuery, moderationActions } from '../../features/admin/reviews/reviewModeration';
import RejectReviewDrawer from './RejectReviewDrawer';
import styles from './Admin.module.css';
import own from './ReviewModeration.module.css';

const PAGE_SIZE = 20;

// الإشراف على التقييمات (المرحلة 13، reviews.moderate): الطابور افتراضياً "بانتظار المراجعة"، اعتماد ورفض بملاحظة للإدارة. سياسة
// النشر (اعتماد تلقائي) إعداد متجر: يراها المشرف، ويغيّرها من يدير إعدادات المتجر (store.settings.manage) — الخادم يحرس الاثنين.
export default function ReviewModeration() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const toast = useToast();
  const [items, setItems] = useState([]);
  const [totalPages, setTotalPages] = useState(1);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('Pending');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [rejecting, setRejecting] = useState(null);
  const [autoApprove, setAutoApprove] = useState(null);
  const canEditPolicy = can('store.settings.manage');

  const load = useCallback(() => {
    setLoading(true);
    api.getAdminReviews(buildReviewQuery({ status, page, pageSize: PAGE_SIZE }))
      .then((res) => { setItems(res.items); setTotalPages(res.totalPages); setError(null); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [status, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { setPage(1); }, [status]);
  useEffect(() => {
    api.getReviewSettings().then((s) => setAutoApprove(s.autoApprove)).catch(() => setAutoApprove(null));
  }, []);

  const approve = async (review) => {
    try {
      await api.approveReview(review.id);
      toast.success(t('admin.reviews.approvedToast'));
      load();
    } catch (e) { toast.error(e.message); }
  };

  const reject = async (note) => {
    await api.rejectReview(rejecting.id, note);
    toast.success(t('admin.reviews.rejectedToast'));
    setRejecting(null);
    load();
  };

  const changePolicy = async (e) => {
    const next = e.target.checked;
    try {
      await api.updateReviewSettings(next);
      setAutoApprove(next);
      toast.success(t('admin.reviews.policySaved'));
    } catch (err) { toast.error(err.message); }
  };

  const statusLabel = (r) => t(`admin.reviews.status.${r.status}`, { defaultValue: r.status });

  const columns = [
    {
      key: 'product', header: t('admin.reviews.colProduct'), width: '170px', truncate: true, tooltip: (r) => r.productName,
      render: (r) => <Link to={`/products/${r.productId}`} target="_blank" rel="noreferrer">{r.productName}</Link>,
    },
    { key: 'customer', header: t('admin.reviews.colCustomer'), width: '140px', truncate: true, tooltip: (r) => r.customerName, render: (r) => r.customerName },
    { key: 'rating', header: t('admin.reviews.colRating'), width: '110px', render: (r) => <StarRating value={r.rating} size={14} /> },
    {
      key: 'comment', header: t('admin.reviews.colComment'), width: '280px', truncate: true, tooltip: (r) => r.comment,
      render: (r) => (
        <>
          <div>{r.comment}</div>
          {r.moderationNote && <div className={styles.nameSecondary}>{t('admin.reviews.noteShort', { note: r.moderationNote })}</div>}
        </>
      ),
    },
    {
      key: 'status', header: t('admin.reviews.colStatus'), width: '120px', truncate: true, tooltip: statusLabel,
      render: (r) => <span className={`${styles.statusBadge} ${styles[STATUS_BADGE[r.status]] ?? ''}`}>{statusLabel(r)}</span>,
    },
    { key: 'date', header: t('admin.reviews.colDate'), width: '110px', render: (r) => formatDate(r.createdAt) },
    {
      key: 'actions', header: t('admin.reviews.colActions'), width: '64px', align: 'end',
      render: (r) => (
        <RowActionsMenu actions={moderationActions(r).map((action) => (action === 'approve'
          ? { label: t('admin.reviews.approve'), onClick: () => approve(r) }
          : { label: t('admin.reviews.reject'), variant: 'danger', onClick: () => setRejecting(r) }))} />
      ),
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.reviews.title')}</h2>
      <p className={styles.pageSub}>{t('admin.reviews.subtitle')}</p>

      {autoApprove !== null && (
        <div className={own.policy}>
          <label className={own.policyToggle}>
            <input type="checkbox" checked={autoApprove} onChange={changePolicy} disabled={!canEditPolicy} />
            {t('admin.reviews.autoApprove')}
          </label>
          <span className={own.policyHint}>
            {t(autoApprove ? 'admin.reviews.autoApproveOnHint' : 'admin.reviews.autoApproveOffHint')}
            {!canEditPolicy && ` ${t('admin.reviews.policyLocked')}`}
          </span>
        </div>
      )}

      <div className={styles.toolbar}>
        <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t('admin.reviews.colStatus')}>
          <option value="">{t('admin.reviews.allStatuses')}</option>
          {REVIEW_STATUSES.map((s) => <option key={s} value={s}>{t(`admin.reviews.status.${s}`)}</option>)}
        </select>
      </div>

      <DataTable columns={columns} rows={items} rowKey={(r) => r.id} loading={loading} error={error}
        onRetry={load} emptyTitle={t('admin.reviews.emptyTitle')} emptyMessage={t('admin.reviews.emptyMessage')}
        minWidth="900px" stickyFirstColumn />

      <Pagination page={page} totalPages={totalPages} onChange={setPage} />

      {rejecting && <RejectReviewDrawer review={rejecting} onReject={reject} onClose={() => setRejecting(null)} />}
    </div>
  );
}
