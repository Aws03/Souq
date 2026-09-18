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
import { PAGE_SIZE, accountState, staffActions } from '../../features/admin/staff/staffView';
import InviteStaffDrawer from './InviteStaffDrawer';
import styles from './Admin.module.css';
import StatusBadge from '../../components/common/StatusBadge';
import { statusTone } from '../../features/statusTone';

// ============================================================================
// فريق المتجر (store.staff.manage، أي مدير المتجر): من يستطيع دخول هذه اللوحة، وبأيّ دور.
// الدعوة تُرسل بريداً برابط على مضيف هذا المتجر؛ الحساب لا كلمة مرور له حتى يقبلها صاحبه.
// الإيقاف يُسقط جلسة الموظّف فوراً على الخادم — لا عند انتهاء توكنه.
// ============================================================================

export default function Staff() {
  const { t } = useTranslation();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [inviting, setInviting] = useState(false);
  const [pendingId, setPendingId] = useState(null);

  const { data, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.staff(page, PAGE_SIZE),
    queryFn: () => api.getStaff({ page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: ['staff'] });

  const invite = async (payload) => {
    const result = await api.inviteStaff(payload);
    toast.success(result?.renewed
      ? t('admin.staff.inviteRenewed', { email: payload.email })
      : t('admin.staff.invited', { email: payload.email }));
    setInviting(false);
    reload();
  };

  const disable = async (account) => {
    await api.setStaffStatus(account.id, false);
    toast.success(t('admin.staff.disabled', { name: account.fullName }));
    reload();
  };

  const run = async (account, action) => {
    if (action === 'disable') {
      confirmation.ask({
        title: t('admin.staff.confirmDisable.title', { name: account.fullName }),
        message: t('admin.staff.confirmDisable.message'),
        confirmLabel: t('admin.staff.confirmDisable.action'),
        danger: true,
        action: () => disable(account),
      });
      return;
    }
    setPendingId(account.id);
    try {
      if (action === 'resend') {
        await api.inviteStaff({ fullName: account.fullName, email: account.email, role: account.role });
        toast.success(t('admin.staff.inviteRenewed', { email: account.email }));
      } else {
        await api.setStaffStatus(account.id, action === 'enable');
        toast.success(t(action === 'enable' ? 'admin.staff.enabled' : 'admin.staff.disabled', { name: account.fullName }));
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
          {a.id === user?.id && <span className={styles.pageSub}> · {t('admin.staff.you')}</span>}
        </>
      ),
    },
    {
      key: 'email', header: t('admin.staff.colEmail'), width: '220px', truncate: true, tooltip: (a) => a.email,
      render: (a) => <span dir="ltr">{a.email}</span>,
    },
    { key: 'role', header: t('admin.staff.colRole'), width: '120px', render: (a) => t(`admin.staff.role.${a.role}`) },
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
      <h2 className={styles.pageTitle}>{t('admin.staff.title')}</h2>
      <p className={styles.pageSub}>{t('admin.staff.subtitle')}</p>

      <div className={styles.toolbar}>
        <Button variant="primary" onClick={() => setInviting(true)}>{t('admin.staff.invite')}</Button>
      </div>

      <DataTable columns={columns} rows={data?.items ?? []} rowKey={(a) => a.id} loading={isPending}
        error={error?.message} onRetry={refetch} emptyTitle={t('admin.staff.emptyTitle')}
        emptyMessage={t('admin.staff.emptyMessage')} minWidth="860px" stickyFirstColumn />

      {data && <Pagination page={page} totalPages={data.totalPages} onChange={setPage} />}

      {inviting && <InviteStaffDrawer onInvite={invite} onClose={() => setInviting(false)} />}
      {confirmation.dialog}
    </div>
  );
}
