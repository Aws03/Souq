import { useEffect, useRef, useState } from 'react';
import { MoreIcon } from '../icons/Icons';
import styles from './RowActionsMenu.module.css';

/**
 * قائمة إجراءات منسدلة لكل صف جدول (زر "⋮" واحد بدل أزرار متفرّقة).
 * actions: [{ label, onClick, variant: 'default' | 'danger', disabled }]
 */
export default function RowActionsMenu({ actions, disabled }) {
  const [open, setOpen] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    if (!open) return;
    const onDocClick = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener('mousedown', onDocClick);
    return () => document.removeEventListener('mousedown', onDocClick);
  }, [open]);

  return (
    <div className={styles.wrap} ref={ref}>
      <button type="button" className={styles.trigger} onClick={() => setOpen((o) => !o)}
        disabled={disabled} aria-haspopup="menu" aria-expanded={open}>
        <MoreIcon />
      </button>
      {open && (
        <div className={styles.menu} role="menu">
          {actions.map((a) => (
            <button key={a.label} type="button" role="menuitem" disabled={a.disabled}
              className={`${styles.item} ${a.variant === 'danger' ? styles.danger : ''}`}
              onClick={() => { setOpen(false); a.onClick(); }}>
              {a.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
