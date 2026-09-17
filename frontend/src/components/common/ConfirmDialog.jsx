import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Button from './Button';
import { useDialog } from './useDialog';
import styles from './ConfirmDialog.module.css';

// ============================================================================
// تأكيد إجراء لا يُتراجع عنه بسهولة — بديل window.confirm حيث يلزم أكثر من "موافق":
//   • يسمّي ما سيحدث وعلى ماذا، بهويّة اللوحة ولغتها واتجاهها (window.confirm نصّ متصفّح عارٍ).
//   • requireText: لإجراءٍ نهائي (أرشفة متجر) يكتب المالك معرّف المتجر — نقرة زرّ مستعجلة لا تُنهي متجراً.
//   • alertdialog بعنوان ووصف مربوطين؛ التركيز يدخل ويعود، وEscape يُلغي، والخلفية لا تُغلق أثناء التنفيذ.
//   • busy/error: الإجراء يُنتظر داخل الحوار، فرفض الخادم يُقرأ في مكانه لا في إشعار عابر بعد إغلاقه.
// ============================================================================
// الجسم يُركَّب مع كل فتح ويُفكّ مع كل إغلاق: ما كُتب للتأكيد لا يبقى لفتحٍ تالٍ — حوار أرشفة ثانٍ لا يفتح
// ومعرّف المتجر الأول مكتوبٌ فيه.
export default function ConfirmDialog({ open, ...props }) {
  return open ? <ConfirmDialogBody {...props} /> : null;
}

function ConfirmDialogBody({
  title, message, confirmLabel, danger = false, requireText = null, busy = false, error = null,
  onConfirm, onCancel, children,
}) {
  const { t } = useTranslation();
  const titleId = useId();
  const messageId = useId();
  const inputId = useId();
  const [typed, setTyped] = useState('');
  const panelRef = useDialog(true, onCancel, { locked: busy });

  const matches = requireText == null || typed.trim() === requireText;

  const submit = (event) => {
    event.preventDefault();
    if (matches && !busy) onConfirm();
  };

  return (
    <div className={styles.layer}>
      <button type="button" className={styles.overlay} aria-label={t('common.cancel')} onClick={onCancel} disabled={busy} />
      <form ref={panelRef} tabIndex={-1} className={styles.dialog} role="alertdialog" aria-modal="true"
        aria-labelledby={titleId} aria-describedby={messageId} onSubmit={submit}>
        <h2 id={titleId} className={styles.title}>{title}</h2>
        <p id={messageId} className={styles.message}>{message}</p>
        {children}
        {requireText != null && (
          <div className={styles.typed}>
            <label htmlFor={inputId} className={styles.typedLabel}>
              {t('common.typeToConfirm', { text: requireText })}
            </label>
            <input id={inputId} className={styles.input} dir="ltr" autoComplete="off" spellCheck={false}
              value={typed} onChange={(e) => setTyped(e.target.value)} />
          </div>
        )}
        {error && <p className={styles.error} role="alert">{error}</p>}
        <div className={styles.actions}>
          <Button variant="ghost" type="button" onClick={onCancel} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant={danger ? 'danger' : 'primary'} type="submit" loading={busy} disabled={!matches}>
            {confirmLabel}
          </Button>
        </div>
      </form>
    </div>
  );
}
