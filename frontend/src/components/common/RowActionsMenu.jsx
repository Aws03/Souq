import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { MoreIcon } from '../icons/Icons';
import styles from './RowActionsMenu.module.css';

/**
 * قائمة إجراءات منسدلة لكل صف جدول (زر "⋮" واحد بدل أزرار متفرّقة).
 * تُرسَم القائمة عبر Portal على body بموضع fixed محسوب من موضع الزر —
 * فلا تقصّها حاوية overflow-x الخاصة بالجدول مهما ضاقت الشاشة. تعمل في
 * RTL وLTR (المحاذاة تُحسَب من اتجاه المستند)، وتُثبَّت داخل حدود النافذة
 * وتنقلب لأعلى قرب أسفلها. تُغلَق بنقرة خارجية أو Escape أو تمرير/تحجيم.
 * actions: [{ label, onClick, variant: 'default' | 'danger', disabled }]
 * label: اسم الزرّ لقارئ الشاشة — أيقونة "⋮" وحدها لا تُقرأ، فكان كل صفّ يُعلَن "زرّ" بلا اسم.
 * يُفضَّل تمرير اسم يحمل الصفّ ("إجراءات Clerk")، فعشرون زرّاً باسم واحد لا تُميَّز.
 */
export default function RowActionsMenu({ actions, disabled, label }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState(null); // null = لم تُقَس بعد؛ تُرسَم مخفية ثم تُموضَع
  const triggerRef = useRef(null);
  const menuRef = useRef(null);

  const close = () => { setOpen(false); setPos(null); };

  // بعد رسم القائمة (مخفية) نقيس حجمها الفعلي ونحسب موضعاً لا يتجاوز حواف
  // النافذة: محاذاة منطقية لحافة الزر حسب الاتجاه، ثم قصر أفقي داخل الشاشة،
  // وانقلاب لأعلى إن لم يتّسع أسفل الزر.
  useLayoutEffect(() => {
    if (!open) return;
    const trigger = triggerRef.current.getBoundingClientRect();
    const menu = menuRef.current.getBoundingClientRect();
    const margin = 8;
    const isRtl = getComputedStyle(triggerRef.current).direction === 'rtl';
    let left = isRtl ? trigger.right - menu.width : trigger.left;
    left = Math.min(Math.max(left, margin), window.innerWidth - menu.width - margin);
    let top = trigger.bottom + 6;
    if (top + menu.height > window.innerHeight - margin) top = trigger.top - menu.height - 6;
    setPos({ top, left });
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onDocDown = (e) => {
      if (triggerRef.current?.contains(e.target) || menuRef.current?.contains(e.target)) return;
      close();
    };
    const onKeyDown = (e) => { if (e.key === 'Escape') close(); };
    // الموضع fixed لا يتبع تمرير الصفحة/الجدول — الإغلاق عندها أبسط وأسلم من إعادة الحساب.
    document.addEventListener('mousedown', onDocDown);
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
    return () => {
      document.removeEventListener('mousedown', onDocDown);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('scroll', close, true);
      window.removeEventListener('resize', close);
    };
  }, [open]);

  return (
    <>
      <button ref={triggerRef} type="button" className={styles.trigger}
        onClick={() => (open ? close() : setOpen(true))}
        disabled={disabled} aria-haspopup="menu" aria-expanded={open} aria-label={label ?? t('common.moreActions')}>
        <MoreIcon />
      </button>
      {open && createPortal(
        <div ref={menuRef} role="menu" className={styles.menu}
          style={pos ? { top: pos.top, left: pos.left } : { visibility: 'hidden', top: 0, left: 0 }}>
          {actions.map((a) => (
            <button key={a.label} type="button" role="menuitem" disabled={a.disabled}
              className={`${styles.item} ${a.variant === 'danger' ? styles.danger : ''}`}
              onClick={() => { close(); a.onClick(); }}>
              {a.label}
            </button>
          ))}
        </div>,
        document.body
      )}
    </>
  );
}
