import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import StarRating from '../../components/product/StarRating';
import { formatDate } from '../../i18n';
import { REVIEW_STATUSES, buildReviewQuery, moderationActions } from '../../features/admin/reviews/reviewModeration';
import RejectReviewDrawer from './RejectReviewDrawer';
import styles from './Admin.module.css';
import own from './ReviewModeration.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { statusTone } from '../../features/statusTone';

const PAGE_SIZE = 20;

// الإشراف على التقييمات (المرحلة 13، reviews.moderate): الطابور افتراضياً "بانتظار المراجعة"، اعتماد ورفض بملاحظة للإدارة. سياسة
// النشر (اعتماد تلقائي) إعداد متجر: يراها المشرف، ويغيّرها من يدير إعدادات المتجر (store.settings.manage) — الخادم يحرس الاثنين.
export default function ReviewModeration() {
  const { t } = useTranslation();
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('Pending');
  const [rejecting, setRejecting] = useState(null);
  const canEditPolicy = can('store.settings.manage');

  // المفتاح يحمل الحالة والصفحة (TD-25، M10): تبديل التصفية بسرعة لا يُظهر نتائج التصفية السابقة.
  const params = buildReviewQuery({ status, page, pageSize: PAGE_SIZE });
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.adminReviews(params),
    queryFn: () => api.getAdminReviews(params),
    placeholderData: keepPreviousData,
  });

  // إعداد الموافقة الآلية شريط تنبيه لا محتوى الشاشة، فخطؤه يُقرأ "غير معروف" (null) كما كان.
  const { data: reviewSettings } = useQuery({
    queryKey: ['admin-review-settings'],
    queryFn: api.getReviewSettings,
  });
  const autoApprove = reviewSettings?.autoApprove ?? null;

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.adminReviewsAll() });
  // في المعالِج لا في تأثير: التصفية سببها ضغطة المستخدم.
  const filterBy = (setter) => (value) => { setter(value); setPage(1); };

  const approve = async (review) => {
    try {
      await api.approveReview(review.id);
      toast.success(t('admin.reviews.approvedToast'));
      reload();
    } catch (e) { toast.error(e.message); }
  };

  const reject = async (note) => {
    await api.rejectReview(rejecting.id, note);
    toast.success(t('admin.reviews.rejectedToast'));
    setRejecting(null);
    reload();
  };

  const changePolicy = async (e) => {
    const next = e.target.checked;
    try {
      await api.updateReviewSettings(next);
      // الردّ يُكتب في الذاكرة المؤقّتة مباشرةً بدل جلبٍ ثانٍ: ما أُرسل هو ما صار عليه الإعداد.
      queryClient.setQueryData(['admin-review-settings'], { autoApprove: next });
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
      render: (r) => <StatusBadge tone={statusTone('review', r.status)}>{statusLabel(r)}</StatusBadge>,
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
        <select value={status} onChange={(e) => filterBy(setStatus)(e.target.value)} aria-label={t('admin.reviews.colStatus')}>
          <option value="">{t('admin.reviews.allStatuses')}</option>
          {REVIEW_STATUSES.map((s) => <option key={s} value={s}>{t(`admin.reviews.status.${s}`)}</option>)}
        </select>
      </div>

      <DataTable label={t('admin.reviews.title')} columns={columns} rows={data?.items ?? []} rowKey={(r) => r.id} loading={isPending}
        error={error?.message} onRetry={refetch} emptyTitle={t('admin.reviews.emptyTitle')} emptyMessage={t('admin.reviews.emptyMessage')}
        minWidth="900px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}

      {rejecting && <RejectReviewDrawer review={rejecting} onReject={reject} onClose={() => setRejecting(null)} />}
    </div>
  );
}
