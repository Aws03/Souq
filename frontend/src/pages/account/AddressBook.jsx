import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useToast } from '../../context/ToastContext';
import Button from '../../components/common/Button';
import { EmptyState } from '../../components/common/StateViews';
import { MapPinIcon, PlusIcon } from '../../components/icons/Icons';
import { formatAddressLine } from '../../features/account/addressForm';
import AddressFormDrawer from './AddressFormDrawer';
import styles from './Account.module.css';

const MAX_ADDRESSES = 20; // Customer.MaxAddresses في Domain

// دفتر العناوين: إضافة وتعديل وحذف وتعيين الافتراضي للشحن/الفوترة. بعد كل تغيير يُعاد تحميل الملف — الخادم هو من
// ينقل صفة الافتراضي عند حذف عنوان افتراضي، فلا تتكرّر تلك القاعدة هنا.
export default function AddressBook({ addresses, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [editing, setEditing] = useState(null); // null=مغلق، {}=إضافة، عنوان=تعديل
  const [busyId, setBusyId] = useState(null);
  const full = addresses.length >= MAX_ADDRESSES;

  // أخطاء الحفظ تُعرض داخل الدرج (AddressFormDrawer يلتقطها).
  const save = async (payload, defaults) => {
    if (editing?.id) await api.updateMyAddress(editing.id, payload);
    else await api.addMyAddress(payload, { defaultShipping: defaults.shipping, defaultBilling: defaults.billing });
    toast.success(t('account.addressSaved'));
    setEditing(null);
    await onChanged();
  };

  const run = async (address, action, successMessage) => {
    setBusyId(address.id);
    try {
      await action();
      if (successMessage) toast.success(successMessage);
      await onChanged();
    } catch (e) { toast.error(e.message); }
    finally { setBusyId(null); }
  };

  return (
    <section className={styles.panel}>
      <div className={styles.panelHead}>
        <h2 className={styles.panelTitle}>{t('account.addressesTitle')}</h2>
        <Button variant="ghost" size="sm" onClick={() => setEditing({})} disabled={full}>
          <PlusIcon size={15} /> {t('account.addAddress')}
        </Button>
      </div>
      {full && <p className={styles.muted}>{t('account.addressLimit', { max: MAX_ADDRESSES })}</p>}

      {addresses.length === 0 ? (
        <EmptyState icon={MapPinIcon} title={t('account.noAddressesTitle')} message={t('account.noAddressesMessage')} />
      ) : (
        <ul className={styles.addressList}>
          {addresses.map((a) => {
            const busy = busyId === a.id;
            return (
              <li key={a.id} className={styles.addressCard}>
                <div className={styles.addressTop}>
                  <span className={styles.addressLabel}>{a.label || a.recipientName}</span>
                  {a.isDefaultShipping && <span className={styles.badge}>{t('account.defaultShipping')}</span>}
                  {a.isDefaultBilling && <span className={styles.badge}>{t('account.defaultBilling')}</span>}
                </div>
                <span className={styles.addressLine}>{formatAddressLine(a)}</span>
                <span className={styles.addressMeta}>{a.recipientName} · <bdi dir="ltr">{a.phone}</bdi></span>
                <div className={styles.addressActions}>
                  <Button variant="link" size="sm" disabled={busy} onClick={() => setEditing(a)}>{t('common.edit')}</Button>
                  {!a.isDefaultShipping && (
                    <Button variant="link" size="sm" disabled={busy}
                      onClick={() => run(a, () => api.setMyDefaultAddress(a.id, 'shipping'))}>
                      {t('account.makeDefaultShipping')}
                    </Button>
                  )}
                  {!a.isDefaultBilling && (
                    <Button variant="link" size="sm" disabled={busy}
                      onClick={() => run(a, () => api.setMyDefaultAddress(a.id, 'billing'))}>
                      {t('account.makeDefaultBilling')}
                    </Button>
                  )}
                  <Button variant="link" size="sm" disabled={busy}
                    onClick={() => run(a, () => api.removeMyAddress(a.id), t('account.addressRemoved'))}>
                    {t('account.removeAddress')}
                  </Button>
                </div>
              </li>
            );
          })}
        </ul>
      )}

      {editing && <AddressFormDrawer address={editing} onSave={save} onClose={() => setEditing(null)} />}
    </section>
  );
}
