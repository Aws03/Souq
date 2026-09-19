import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useDebouncedValue } from '../../hooks/useDebouncedValue';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Pagination from '../../components/common/Pagination';
import { SearchIcon } from '../../components/icons/Icons';
import { formatPrice } from '../../components/product/ProductBadges';
import { formatDate } from '../../i18n';
import { ORDER_STATUSES, buildOrderQuery } from '../../features/orders/orderView';
import OrderDetailDrawer from './OrderDetailDrawer';
import styles from './Admin.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { statusTone } from '../../features/statusTone';

const PAGE_SIZE = 20;

// شاشة طلبات المتجر (المرحلة 9): رقم الطلب داخل المتجر، اسم العميل، بحث (رقم الطلب أو اسم العميل أو بريده) وتصفية
// بالحالة. الإجراءات في درج الطلب كما يعيدها الخادم من جدول الانتقالات (allowedActions) — لا نسخة ثانية للقاعدة هنا.
//
// **على طبقة الاستعلام (TD-25، M10).** البحث هو ما يجعل العيب القديم ملموساً هنا أكثر من الترقيم: كتابة
// "أحمد" كانت تُطلق طلباً لكل حرف، فإن وصل ردّ "أح" بعد ردّ "أحمد" كُتب فوقه — جدولُ نتائجِ بحثٍ لم
// يعد مكتوباً في الصندوق. المفتاح يحمل معايير العرض كلّها، فكلّ ردّ يُكتب في مفتاحه لا على الشاشة.
// والبحث مُهدَّأ كما في نظيرتها على المنصّة (platform/Stores.jsx): حرفٌ واحد لا يستحقّ رحلة.
export default function Orders() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [openId, setOpenId] = useState(null);
  const term = useDebouncedValue(search.trim());

  const params = buildOrderQuery({ status, search: term, page, pageSize: PAGE_SIZE });
  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.adminOrders(params),
    queryFn: () => api.getOrders(params),
    placeholderData: keepPreviousData,
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.adminOrdersAll() });

  // العودة إلى الصفحة الأولى تحدث في المعالِج لا في تأثير: تصفيةٌ جديدة **سببها** ضغطةُ المستخدم،
  // وضبطُ حالةٍ داخل تأثيرٍ يُنتج عرضاً متتالياً ويُخفي السبب (react-hooks/set-state-in-effect).
  // الفرق المرصود الوحيد عن السلوك السابق: كتابةٌ ثم حذفُها بالكامل وأنت على صفحةٍ بعد الأولى تُعيدك
  // إلى الأولى الآن — لأن اللمسة نفسها هي المعيار، لا القيمة المُهدَّأة. لا اختبار يرصده، ولا هو عيب.
  const filterBy = (setter) => (value) => { setter(value); setPage(1); };

  const statusLabel = (o) => t(`admin.orders.status.${o.status}`, { defaultValue: o.status });

  // عرض/محاذاة ثابتان لكل عمود (colgroup في DataTable) — لا يهتزّ الجدول بين صفحات بأطوال قيم مختلفة.
  const columns = [
    { key: 'number', header: t('admin.orders.colNumber'), width: '84px', align: 'end', render: (o) => `#${o.orderNumber}` },
    {
      key: 'customer', header: t('admin.orders.colCustomer'), width: '170px', truncate: true,
      tooltip: (o) => o.customerName ?? `#${o.customerId}`, render: (o) => o.customerName ?? `#${o.customerId}`,
    },
    {
      key: 'status', header: t('admin.orders.colStatus'), width: '130px', truncate: true, tooltip: statusLabel,
      render: (o) => <StatusBadge tone={statusTone('order', o.status)}>{statusLabel(o)}</StatusBadge>,
    },
    { key: 'count', header: t('admin.orders.colItems'), width: '80px', align: 'end', render: (o) => o.itemCount },
    { key: 'total', header: t('admin.orders.colTotal'), width: '120px', align: 'end', render: (o) => formatPrice(o.totalAmount, o.currency) },
    { key: 'date', header: t('admin.orders.colDate'), width: '110px', render: (o) => formatDate(o.createdAt) },
    {
      key: 'actions', header: t('admin.orders.colActions'), width: '64px', align: 'end',
      render: (o) => <RowActionsMenu actions={[{ label: t('admin.orders.view'), onClick: () => setOpenId(o.id) }]} />,
    },
  ];

  return (
    <div>
      <h2 className={styles.pageTitle}>{t('admin.orders.title')}</h2>
      <p className={styles.pageSub}>{t('admin.orders.subtitle')}</p>

      <div className={styles.toolbar}>
        <label className={styles.search}>
          <SearchIcon size={16} />
          <input value={search} onChange={(e) => filterBy(setSearch)(e.target.value)} placeholder={t('admin.orders.searchPlaceholder')} />
        </label>
        <select value={status} onChange={(e) => filterBy(setStatus)(e.target.value)} aria-label={t('admin.orders.colStatus')}>
          <option value="">{t('admin.orders.allStatuses')}</option>
          {ORDER_STATUSES.map((s) => <option key={s} value={s}>{t(`admin.orders.status.${s}`)}</option>)}
        </select>
      </div>

      <DataTable label={t('admin.orders.title')} columns={columns} rows={data?.items ?? []} rowKey={(o) => o.id} loading={isPending}
        error={error?.message} onRetry={refetch} emptyTitle={t('admin.orders.emptyTitle')}
        emptyMessage={t('admin.orders.emptyMessage')} minWidth="720px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}

      {openId && <OrderDetailDrawer orderId={openId} onClose={() => setOpenId(null)} onChanged={reload} />}
    </div>
  );
}
