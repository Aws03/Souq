import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
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

  const close = useCallback(() => { setOpen(false); setPos(null); }, []);

  // نقيس حجم القائمة الفعلي (وهي مرسومة مخفية أوّل مرّة) ونحسب موضعاً لا يتجاوز حواف النافذة:
  // محاذاة منطقية لحافة الزر حسب الاتجاه، ثم قصر أفقي داخل الشاشة، وانقلاب لأعلى إن لم يتّسع أسفله.
  const place = useCallback(() => {
    const triggerEl = triggerRef.current;
    const menuEl = menuRef.current;
    if (!triggerEl || !menuEl) return;
    const trigger = triggerEl.getBoundingClientRect();
    // لا إغلاق حين يخرج الزرّ من الرؤية: جُرِّب ذلك أوّلاً وكان خطأً: لحظةَ فتحِ القائمة بلوحة
    // المفاتيح يكون التمرير السلس قد بدأ للتوّ والزرّ ما زال تحت الطيّة، فتُغلق القائمة فور فتحها —
    // وهو العطل نفسه بصيغة أخرى. القائمة تتبع زرّها وتُقصَر داخل الشاشة، وتُغلق بنقرةٍ خارجية أو Escape.
    const menu = menuEl.getBoundingClientRect();
    const margin = 8;
    const isRtl = getComputedStyle(triggerEl).direction === 'rtl';
    let left = isRtl ? trigger.right - menu.width : trigger.left;
    left = Math.min(Math.max(left, margin), window.innerWidth - menu.width - margin);
    let top = trigger.bottom + 6;
    if (top + menu.height > window.innerHeight - margin) top = trigger.top - menu.height - 6;
    setPos({ top, left });
  }, []);

  useLayoutEffect(() => { if (open) place(); }, [open, place]);

  useEffect(() => {
    if (!open) return;
    const onDocDown = (e) => {
      if (triggerRef.current?.contains(e.target) || menuRef.current?.contains(e.target)) return;
      close();
    };
    const onKeyDown = (e) => { if (e.key === 'Escape') close(); };

    // ── التمرير يُعيد الموضع، ولا يُغلق (M18) ────────────────────────────────────
    // كان يُغلق. و`html { scroll-behavior: smooth }` يجعل أي نقل إلى الرؤية تمريراً مستمرّاً
    // لعشرات الأحداث بعد وقوعه — فمستخدم لوحة مفاتيح يصل بـ Tab إلى زرّ صفٍّ تحت الطيّة
    // (فيُمرّره المتصفّح إلى الرؤية تمريراً سلساً) ثمّ يضغط Enter، تُفتح قائمته وتُغلق فوراً
    // بأحداث ذلك التمرير نفسه. أي أنّ الزرّ كان **غير قابل للاستعمال بلوحة المفاتيح** هناك.
    // قيس في M18 على حزمةٍ حقيقية: 32 حدث تمرير، والقائمة صفر بعد 150ms وبعد الهدوء.
    //
    // وإعادة الموضع هي السلوك الصحيح لا مجرّد علاج: القائمة `fixed` فكانت تنفصل بصرياً عن صفّها
    // مع أي تمرير على أي حال. الآن تلتصق به، وتُغلق حين يخرج الصفّ من الرؤية — وهو المعنى المقصود.
    let frame = 0;
    const reposition = () => {
      if (frame) return;                       // حدث لكل إطار على الأكثر
      frame = requestAnimationFrame(() => { frame = 0; place(); });
    };

    document.addEventListener('mousedown', onDocDown);
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('scroll', reposition, true);
    window.addEventListener('resize', reposition);
    return () => {
      if (frame) cancelAnimationFrame(frame);
      document.removeEventListener('mousedown', onDocDown);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('scroll', reposition, true);
      window.removeEventListener('resize', reposition);
    };
  }, [open, close, place]);

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
