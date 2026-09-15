import { useEffect, useRef } from 'react';

// ============================================================================
// سلوك النافذة الحوارية الذي كان ناقصاً في كل أدراج المتجر ولوحة الإدارة (المرحلة 16):
//   • Escape يغلق. كان الإغلاق بالنقر على الخلفية أو زرّ X فقط — من لا يستعمل فأرة كان
//     يبقى داخل الدرج.
//   • التركيز ينتقل إلى الدرج عند فتحه ويعود إلى ما فتحه عند إغلاقه، فلا يتيه قارئ الشاشة
//     في الصفحة خلف الدرج.
//   • تمرير الصفحة خلف الدرج يتوقّف — الدرج مشروط بـaria-modal، وصفحة تتحرّك تحته تكذّب ذلك.
//
// حبس التركيز الكامل (Tab دائري) ليس هنا: يحتاج إدارة قائمة العناصر القابلة للتركيز، وهو
// تحسين لاحق موثّق لا ادّعاء صامت (FrontendGuide، إتاحة).
// ============================================================================
export function useDialog(open, onClose, { locked = false } = {}) {
  const panelRef = useRef(null);
  const openerRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;

    openerRef.current = document.activeElement;
    panelRef.current?.focus();

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    const onKeyDown = (event) => {
      if (event.key === 'Escape' && !locked) onClose?.();
    };
    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.body.style.overflow = previousOverflow;
      // العودة إلى ما فتح الدرج — إن كان ما زال في المستند.
      const opener = openerRef.current;
      if (opener instanceof HTMLElement && document.contains(opener)) opener.focus();
    };
  }, [open, onClose, locked]);

  return panelRef;
}
