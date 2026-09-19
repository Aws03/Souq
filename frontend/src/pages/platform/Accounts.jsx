import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import Pagination from '../../components/common/Pagination';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import Button from '../../components/common/Button';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import { formatDateTime } from '../../i18n';
import { PAGE_SIZE, PLATFORM_ROLES, accountState, staffActions } from '../../features/admin/staff/staffView';
import InviteStaffDrawer from '../admin/InviteStaffDrawer';
import styles from './Platform.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { statusTone } from '../../features/statusTone';

// ============================================================================
// حسابات المنصّة (platform.users.manage — المالك وحده): من يدخل هذه اللوحة، وبأيّ دور.
//
// ما يقدّمه الخادم فقط: دعوة (مالك أو مشرف)، وإعادة الدعوة لحساب لم يقبل، وتفعيل وإيقاف. لا تغيير دور لحساب مفعّل
// ولا حذف — فلا يُعرضان. الدعوة على مضيف المنصّة نفسه، والإيقاف يُسقط جلسة الحساب فوراً.
// القواعد على الخادم (لا إيقاف للنفس، لا إيقاف لآخر مالك فعّال)؛ الواجهة تُخفي "إيقاف" عن صفّ المستخدم نفسه
// توفيراً لخطأ، وتعرض رفض آخر مالك داخل حوار التأكيد كما يقوله الخادم.
// ============================================================================
export default function Accounts() {
  const { t } = useTranslation();
  const toast = useToast();
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const confirmation = useConfirmAction();
  const [page, setPage] = useState(1);
  const [inviting, setInviting] = useState(false);
  const [pendingId, setPendingId] = useState(null);

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformUsers(page, PAGE_SIZE),
    queryFn: () => api.getPlatformUsers({ page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: ['platform-users'] });

  const invite = async (payload) => {
    const result = await api.invitePlatformUser(payload);
    toast.success(t(result?.renewed ? 'platform.accounts.inviteRenewed' : 'platform.accounts.invited', { email: payload.email }));
    setInviting(false);
    reload();
  };

  const disable = (account) => confirmation.ask({
    title: t('platform.accounts.confirmDisable.title', { name: account.fullName }),
    message: t('platform.accounts.confirmDisable.message'),
    confirmLabel: t('platform.accounts.confirmDisable.action'),
    danger: true,
    action: async () => {
      await api.setPlatformUserStatus(account.id, false);
      toast.success(t('platform.accounts.disabled', { name: account.fullName }));
      reload();
    },
  });

  const run = async (account, action) => {
    if (action === 'disable') { disable(account); return; }
    setPendingId(account.id);
    try {
      if (action === 'resend') {
        await api.invitePlatformUser({ fullName: account.fullName, email: account.email, role: account.role });
        toast.success(t('platform.accounts.inviteRenewed', { email: account.email }));
      } else {
        await api.setPlatformUserStatus(account.id, true);
        toast.success(t('platform.accounts.enabled', { name: account.fullName }));
      }
      reload();
    } catch (err) {
      toast.error(err.message);
    } finally {
      setPendingId(null);
    }
  };

  const columns = [
    {
      key: 'name', header: t('admin.staff.colName'), width: '190px', truncate: true, tooltip: (a) => a.fullName,
      render: (a) => (
        <>
          {a.fullName}
          {a.id === user?.id && <span> · {t('admin.staff.you')}</span>}
        </>
      ),
    },
    {
      key: 'email', header: t('admin.staff.colEmail'), width: '220px', truncate: true, tooltip: (a) => a.email,
      render: (a) => <span dir="ltr">{a.email}</span>,
    },
    { key: 'role', header: t('admin.staff.colRole'), width: '130px', render: (a) => t(`platform.accounts.role.${a.role}`) },
    {
      key: 'status', header: t('admin.staff.colStatus'), width: '130px', render: (a) => {
        const state = accountState(a);
        return <StatusBadge tone={statusTone('account', state)}>{t(`admin.staff.state.${state}`)}</StatusBadge>;
      },
    },
    {
      key: 'lastLogin', header: t('admin.staff.colLastLogin'), width: '150px',
      render: (a) => (a.lastLoginAt ? formatDateTime(a.lastLoginAt) : t('admin.staff.never')),
    },
    {
      key: 'actions', header: t('admin.staff.colActions'), width: '64px', align: 'end', render: (a) => {
        const actions = staffActions(a, user?.id);
        if (actions.length === 0) return null;
        return (
          <RowActionsMenu disabled={pendingId === a.id} label={t('admin.staff.actionsFor', { name: a.fullName })}
            actions={actions.map((action) => ({
              label: t(`admin.staff.action.${action}`),
              variant: action === 'disable' ? 'danger' : 'default',
              onClick: () => run(a, action),
            }))} />
        );
      },
    },
  ];

  return (
    <div>
      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>{t('platform.accounts.title')}</h1>
          <p className={styles.subtitle}>{t('platform.accounts.subtitle')}</p>
        </div>
        <Button variant="primary" onClick={() => setInviting(true)}>{t('platform.accounts.invite')}</Button>
      </div>

      <DataTable label={t('platform.accounts.title')} columns={columns} rows={data?.items ?? []} rowKey={(a) => a.id} loading={isPending}
        error={error?.message} onRetry={refetch} emptyTitle={t('platform.accounts.emptyTitle')}
        emptyMessage={t('platform.accounts.emptyMessage')} minWidth="880px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}

      {inviting && (
        <InviteStaffDrawer roles={PLATFORM_ROLES} defaultRole="PlatformAdmin" ns="platform.accounts"
          onInvite={invite} onClose={() => setInviting(false)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
