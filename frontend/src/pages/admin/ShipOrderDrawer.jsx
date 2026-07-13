import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import styles from './ShipOrderDrawer.module.css';

// درج شحن طلب: رقم تتبّع وشركة شحن اختياريان (قد لا تتوفّران لحظة الشحن نفسها)
// + ملاحظة اختيارية تُسجَّل في سجلّ تاريخ الطلب. onShip يستدعي فعلياً
// api.updateOrderStatus(order.id, 'Ship', { trackingNumber, shippingCarrier, note }).
export default function ShipOrderDrawer({ order, onShip, onClose }) {
  const { t } = useTranslation();
  const [trackingNumber, setTrackingNumber] = useState('');
  const [shippingCarrier, setShippingCarrier] = useState('');
  const [note, setNote] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true); setError(null);
    try {
      await onShip({
        trackingNumber: trackingNumber.trim() || null,
        shippingCarrier: shippingCarrier.trim() || null,
        note: note.trim() || null,
      });
    } catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy}
      title={t('admin.orders.shipTitle', { id: order.id })}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="ship-order-form" loading={busy}>{t('admin.orders.confirmShip')}</Button>
        </div>
      }>
      <form id="ship-order-form" onSubmit={submit}>
        {error && <ErrorBanner message={error} />}

        <FormField label={t('admin.orders.trackingNumberLabel')} hint={t('admin.orders.trackingNumberHint')}>
          <input className={inputClass(false)} value={trackingNumber}
            onChange={(e) => setTrackingNumber(e.target.value)} dir="ltr" />
        </FormField>

        <FormField label={t('admin.orders.carrierLabel')}>
          <input className={inputClass(false)} value={shippingCarrier}
            onChange={(e) => setShippingCarrier(e.target.value)} placeholder={t('admin.orders.carrierPlaceholder')} />
        </FormField>

        <FormField label={t('admin.orders.noteLabel')}>
          <textarea className={inputClass(false)} rows={3} value={note} onChange={(e) => setNote(e.target.value)} />
        </FormField>
      </form>
    </Drawer>
  );
}
