import { useEffect, useRef } from 'react';

// ============================================================================
// سلوك النافذة الحوارية الذي كان ناقصاً في كل أدراج المتجر ولوحة الإدارة (المرحلة 16):
//   • Escape يغلق. كان الإغلاق بالنقر على الخلفية أو زرّ X فقط — من لا يستعمل فأرة كان
//     يبقى داخل الدرج.
//   • التركيز ينتقل إلى الدرج عند فتحه ويعود إلى ما فتحه عند إغلاقه، فلا يتيه قارئ الشاشة
//     في الصفحة خلف الدرج.
//   • تمرير الصفحة خلف الدرج يتوقّف — الدرج مشروط بـaria-modal، وصفحة تتحرّك تحته تكذّب ذلك.
//   • **Tab لا يخرج من الدرج** (TD-48، أُضيف بعد المرحلة 16): يدور داخله. بدونه كان مستعمل
//     لوحة المفاتيح يخرج إلى الصفحة خلف الدرج ويتفاعل مع عناصر تحجبها الطبقة فوقها وهو لا
//     يراها — والدرج يعلن `aria-modal` طوال ذلك، فيكذّبه Tab تماماً كما تكذّبه صفحة تتمرّر تحته.
//
// حوار فوق درج (تأكيد حذف صورة داخل درج المنتج): Escape يغلق الأعلى وحده — مكدّس الحوارات المفتوحة. وonClose
// وlocked يُقرآن من مرجع: دالة جديدة في كل عرض (أو busy يتغيّر) لا تعيد تشغيل الأثر فتنقل التركيز من مكانه.
// وحبس التركيز يتبع المكدّس نفسه: الدرج الأعلى وحده يحبس، فلا يتنازع درجان على Tab واحد.
// ============================================================================

// ما يُعدّ قابلاً للتركيز. `:not([disabled])` و`tabindex="-1"` يُستبعدان: الأول لا يُركَّز
// أصلاً، والثاني يُركَّز بالبرمجة لا بـTab — ولوحة الدرج نفسها من النوع الثاني.
const FOCUSABLE = [
  'a[href]', 'area[href]', 'button', 'input', 'select', 'textarea',
  '[tabindex]', 'audio[controls]', 'video[controls]', '[contenteditable]',
].map((selector) => `${selector}:not([disabled]):not([tabindex="-1"]):not([aria-hidden="true"])`).join(',');

// ============================================================================
// المرئي وحده يدخل الدورة: عنصرٌ في تبويب مخفي لا يُركَّز، وإدراجه يجعل الدورة تقف عند شيء لا
// يراه أحد.
//
// و`checkVisibility()` لا `offsetParent`، والفرق ليس أسلوبياً: `offsetParent` فارغ لأي عنصر
// `position: fixed` — وهو ما تستعمله الأدراج والحوارات نفسها — فكان الفحص يُسقط كل شيء أحياناً
// **فيُعطّل الحبس بصمت**. وقد وقع ذلك فعلاً في أول تشغيل: jsdom بلا تخطيط، فـ`offsetParent`
// فارغ دائماً، والاختبار أظهر الحبس معطّلاً بالكامل.
//
// والبديل عند غياب الدالّة هو **الإدراج** لا الإسقاط: إدراج عنصر مخفي يكلّف نقرة Tab زائدة،
// وإسقاط الجميع يفتح الصفحة خلف الحوار. الخطأ الآمن واضح أيّهما.
// ============================================================================
const visibleFocusable = (panel) =>
  Array.from(panel?.querySelectorAll(FOCUSABLE) ?? [])
    .filter((element) => (typeof element.checkVisibility === 'function' ? element.checkVisibility() : true));
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
      if (openDialogs[openDialogs.length - 1] !== token) return;

      if (event.key === 'Escape') {
        if (!lockedRef.current) onCloseRef.current?.();
        return;
      }

      // ====================================================================
      // الدورة عند الطرفين فقط: Tab في وسط الدرج يُترك للمتصفّح، فترتيبه الطبيعي أدقّ من أي
      // ترتيب نعيد حسابه. والقائمة تُقرأ عند كل ضغطة لا عند الفتح: محتوى الدرج يتغيّر
      // (تبويب يُبدَّل، حقل يظهر)، وقائمةٌ محفوظة عند الفتح تصير خاطئة بعد أول تفاعل.
      // ====================================================================
      if (event.key !== 'Tab') return;
      const panel = panelRef.current;
      if (!panel) return;

      const focusable = visibleFocusable(panel);
      if (focusable.length === 0) {
        // درجٌ بلا عنصر قابل للتركيز: يبقى التركيز على لوحته بدل أن يهرب إلى ما خلفها.
        event.preventDefault();
        panel.focus();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;

      // التركيز على اللوحة نفسها (حال الفتح) ⇒ Tab يدخل أوّل عنصر، وShift+Tab يدخل آخره.
      if (active === panel) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
        return;
      }

      if (!panel.contains(active)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
        return;
      }

      if (event.shiftKey && active === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
      }
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
