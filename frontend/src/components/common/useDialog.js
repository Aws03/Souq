import { useEffect, useRef } from 'react';

// ============================================================================
// سلوك النافذة الحوارية الذي كان ناقصاً في كل أدراج المتجر ولوحة الإدارة (المرحلة 16):
//   • Escape يغلق. كان الإغلاق بالنقر على الخلفية أو زرّ X فقط — من لا يستعمل فأرة كان
//     يبقى داخل الدرج.
//   • التركيز ينتقل إلى الدرج عند فتحه ويعود إلى ما فتحه عند إغلاقه، فلا يتيه قارئ الشاشة
//     في الصفحة خلف الدرج.
//   • تمرير الصفحة خلف الدرج يتوقّف — الدرج مشروط بـaria-modal، وصفحة تتحرّك تحته تكذّب ذلك.
//
// حوار فوق درج (تأكيد حذف صورة داخل درج المنتج): Escape يغلق الأعلى وحده — مكدّس الحوارات المفتوحة. وonClose
// وlocked يُقرآن من مرجع: دالة جديدة في كل عرض (أو busy يتغيّر) لا تعيد تشغيل الأثر فتنقل التركيز من مكانه.
//
// حبس التركيز الكامل (Tab دائري) ليس هنا: يحتاج إدارة قائمة العناصر القابلة للتركيز، وهو
// تحسين لاحق موثّق لا ادّعاء صامت — FrontendGuide.md §11 و TD-48 في TechnicalDebt.md.
// ============================================================================
const openDialogs = [];

export function useDialog(open, onClose, { locked = false } = {}) {
  const panelRef = useRef(null);
  const openerRef = useRef(null);
  const onCloseRef = useRef(onClose);
  const lockedRef = useRef(locked);
  useEffect(() => {
    onCloseRef.current = onClose;
    lockedRef.current = locked;
  });

  useEffect(() => {
    if (!open) return undefined;

    const token = {};
    openDialogs.push(token);
    openerRef.current = document.activeElement;
    panelRef.current?.focus();

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    const onKeyDown = (event) => {
      if (event.key !== 'Escape' || openDialogs[openDialogs.length - 1] !== token) return;
      if (!lockedRef.current) onCloseRef.current?.();
    };
    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.removeEventListener('keydown', onKeyDown);
      openDialogs.splice(openDialogs.indexOf(token), 1);
      document.body.style.overflow = previousOverflow;
      // العودة إلى ما فتح الدرج — إن كان ما زال في المستند.
      const opener = openerRef.current;
      if (opener instanceof HTMLElement && document.contains(opener)) opener.focus();
    };
  }, [open]);

  return panelRef;
}
