import { useEffect, useRef, useState } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAuth } from '../context/AuthContext';

// ============================================================================
// طبقة حالة الخادم (ADR-0037، TD-23) — اعتُمدت في المرحلة 16 عند إعادة بناء واجهة المتجر،
// وهي نقطة التبنّي التي سمّاها القرار نفسه.
//
// ما تحلّه فعلاً هنا، لا نظرياً:
//   • حارس الإلغاء المكتوب بيد في كل شاشة (TD-25) — المكتبة تتجاهل ردّ استعلام لم يعد مطلوباً.
//   • الرجوع بالمتصفّح كان يعيد تحميل كل شيء من الصفر ويعرض هيكلاً عظمياً مكان صفحة كانت
//     معروضة قبل ثانية. الآن تُعرض النسخة المحفوظة فوراً ويُحدَّث خلفها.
//   • تكرار نفس الطلب من مكوّنين في نفس الصفحة.
//
// الإعدادات مقصودة:
//   staleTime = 0  — كل دخول إلى شاشة يعيد التحقّق. هذا متجر: السعر والمخزون وحالة الطلب
//                    يجب ألّا تُعرض "طازجة" وهي قديمة. القديم يُعرض فوراً ويُستبدل عند الوصول.
//   retry      — أخطاء العميل (4xx) لا تُعاد: منتج غير موجود سيبقى غير موجود، وإعادة المحاولة
//                ثلاثاً تعني ثلاث 404 وتأخيراً بلا فائدة. أخطاء الخادم والشبكة تُعاد مرّة.
//   refetchOnWindowFocus = false — تبويب مفتوح منذ ساعة لا يجب أن يُطلق موجة طلبات لحظة العودة.
//
// وأهمّها أماناً: تبدّل هوية المستخدم يُلقي الذاكرة المؤقّتة كلّها. خروجٌ ثم دخول بحساب آخر على
// الجهاز نفسه كان سيعرض طلبات الأول للثاني لولا ذلك — والخادم ما كان ليُسأل أصلاً.
// ============================================================================
const createClient = () => new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 0,
      gcTime: 5 * 60 * 1000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        const status = error?.status;
        if (typeof status === 'number' && status >= 400 && status < 500) return false;
        return failureCount < 1;
      },
    },
  },
});

export function QueryProvider({ children }) {
  const { user } = useAuth();
  const [client] = useState(createClient);
  const identity = user?.id ?? null;

  // المسح عند *تبدّل* الهوية لا عند أول تركيب. تأثيرات React تعمل من الابن إلى الأب، فمسحٌ
  // غير مشروط في أول تركيب يُلغي استعلامات بدأتها الشاشة للتوّ — وتبقى معلّقة إلى الأبد.
  //
  // والقاعدة عمداً بسيطة: أي تبدّل يمسح، بما فيه تسجيل الدخول من حالة زائر. ثمنه إعادة جلب
  // بيانات عامة، ومقابله قاعدة أمان لا استثناء فيها يُنسى.
  //
  // resetQueries لا clear: clear يُفرغ الذاكرة لكن المراقِب المركَّب يحتفظ بآخر بيانات رآها
  // ولا يُعيد الطلب — فتبقى شاشة "طلباتي" تعرض طلبات الأول للثاني حتى يتنقّل. resetQueries
  // يعيد كل استعلام إلى حالته الابتدائية ويُعيد طلب النشِط منها. (اكتُشف باختبار، لا بقراءة.)
  const previousIdentity = useRef(identity);
  useEffect(() => {
    if (previousIdentity.current === identity) return;
    previousIdentity.current = identity;
    client.resetQueries();
  }, [client, identity]);

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
