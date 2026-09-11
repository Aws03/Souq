import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { formatDateTime } from '../../i18n';
import { BellIcon } from '../icons/Icons';
import { POLL_MS, badgeLabel, describeNotification } from '../../features/notifications/notificationView';
import styles from './NotificationBell.module.css';

// ============================================================================
// جرس الإشعارات (المرحلة 14): عدد غير المقروء يُسأل كل دقيقة، والقائمة تُجلب عند الفتح. النقر على إشعار يعلّمه مقروءاً وينتقل
// لصفحته. المصدر واحد (/api/notifications) للعميل (طلباته) وللإدارة (طلب جديد، مخزون ينفد) — والنصّ بلغة الزائر. بلا جلسة لا
// شيء. الإغلاق بنقرة خارجية أو Escape.
// ============================================================================
export default function NotificationBell({ className = '' }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();
  const [unread, setUnread] = useState(0);
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState(null);
  const [error, setError] = useState(null);
  const rootRef = useRef(null);

  const refreshCount = useCallback(() => {
    api.getUnreadNotificationCount().then((r) => setUnread(r.count)).catch(() => {});
  }, []);

  useEffect(() => {
    if (!isAuthenticated) {
      setUnread(0);
      return undefined;
    }
    refreshCount();
    const timer = setInterval(refreshCount, POLL_MS);
    return () => clearInterval(timer);
  }, [isAuthenticated, refreshCount]);

  useEffect(() => {
    if (!open) return undefined;
    setError(null);
    api.getNotifications({ pageSize: 10 }).then((page) => setItems(page.items)).catch((e) => setError(e.message));
    const onDown = (e) => { if (!rootRef.current?.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', onDown);
    window.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      window.removeEventListener('keydown', onKey);
    };
  }, [open]);

  if (!isAuthenticated) return null;

  const openItem = async (notification) => {
    const { link } = describeNotification(notification, t);
    setOpen(false);
    if (!notification.isRead) {
      try { await api.markNotificationRead(notification.id); } catch { /* تبقى غير مقروءة — لا تمنع الانتقال */ }
      refreshCount();
    }
    if (link) navigate(link);
  };

  const markAll = async () => {
    try {
      await api.markAllNotificationsRead();
      setItems((list) => list?.map((n) => ({ ...n, isRead: true })) ?? list);
      setUnread(0);
    } catch (e) { setError(e.message); }
  };

  return (
    <div className={`${styles.root} ${className}`} ref={rootRef}>
      <button type="button" className={styles.trigger} onClick={() => setOpen((o) => !o)}
        aria-haspopup="true" aria-expanded={open} aria-label={t('notifications.aria', { count: unread })}>
        <BellIcon size={18} />
        {unread > 0 && <span className={styles.badge}>{badgeLabel(unread)}</span>}
      </button>

      {open && (
        <div className={styles.panel} role="dialog" aria-label={t('notifications.title')}>
          <div className={styles.head}>
            <strong>{t('notifications.title')}</strong>
            {unread > 0 && (
              <button type="button" className={styles.markAll} onClick={markAll}>{t('notifications.markAll')}</button>
            )}
          </div>
          {error && <p className={styles.note}>{error}</p>}
          {!error && items === null && <p className={styles.note}>{t('notifications.loading')}</p>}
          {items?.length === 0 && <p className={styles.note}>{t('notifications.empty')}</p>}
          {items?.length > 0 && (
            <ul className={styles.list}>
              {items.map((n) => (
                <li key={n.id}>
                  <button type="button" className={`${styles.item} ${n.isRead ? '' : styles.unread}`} onClick={() => openItem(n)}>
                    <span>{describeNotification(n, t).text}</span>
                    <time className={styles.time} dateTime={n.createdAt}>{formatDateTime(n.createdAt)}</time>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
