import { useState } from 'react';
import ConfirmDialog from './ConfirmDialog';

// ============================================================================
// تأكيد ثم تنفيذ، بنمط واحد لكل شاشات الإدارة (بديل window.confirm):
//   ask({ title, message, confirmLabel, danger, requireText, action }) يفتح الحوار؛ action لا تُستدعى إلا بالتأكيد.
//   الحوار يبقى مفتوحاً أثناء التنفيذ (لا إلغاء في منتصفه)، ويُغلق بالنجاح وحده. رفض الخادم يُقرأ داخل الحوار
//   نفسه لا في إشعار عابر — فيعرف المدير أن ما أكّده لم يحدث، ويستطيع الإلغاء أو المحاولة مجدّداً.
// النجاح وإشعاره وإعادة التحميل من شأن action — الخطّاف لا يعرف ما الذي حُذف.
// ============================================================================
export function useConfirmAction() {
  const [request, setRequest] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const ask = (config) => { setError(null); setRequest(config); };

  const cancel = () => {
    if (busy) return;
    setRequest(null);
    setError(null);
  };

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await request.action();
      setRequest(null);
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const { action: _action, ...dialog } = request ?? {};
  const element = (
    <ConfirmDialog open={!!request} busy={busy} error={error} {...dialog} onConfirm={confirm} onCancel={cancel} />
  );
  return { ask, dialog: element, pending: !!request };
}
