import { useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useDebouncedValue } from '../../hooks/useDebouncedValue';

// ============================================================================
// اقتراحات البحث أثناء الكتابة (M3، ADR-0042).
//
// الاقتراحات تأتي من الخادم كاملةً: هو من يطبّع النصّ ويطابق ويرتّب. الواجهة لا تصفّي ولا تُعيد ترتيباً ولا
// تُخفي عنصراً — وإلا لاختلف ما يراه المتسوّق في القائمة عمّا يراه في صفحة النتائج للكلمة نفسها.
//
// أقصر من MinKeywordLength لا يُطلَب إطلاقاً: حرف واحد يطابق نصف الكتالوج، فالطلب كلفة بلا معلومة.
// والتأخير (debounce) هنا **غير** تأخير صندوق البحث في useStoreSearch: ذاك يؤخّر الكتابة في الرابط،
// وهذا يؤخّر طلب الاقتراحات. كلاهما مطلوب، وليسا تأخيراً مضاعفاً على المسار نفسه.
// ============================================================================
const MIN_KEYWORD_LENGTH = 2;
const DEBOUNCE_MS = 200;
const EMPTY = [];

/**
 * @param {string} keyword
 * @param {boolean} enabled صندوق البحث مركَّز ولم يُغلق المتسوّق القائمة
 * @returns {{items: Array<{kind: string, id: number, slug: string, name: string, imageUrl: string|null}>,
 *            loading: boolean}}
 */
export function useSearchSuggestions(keyword, enabled) {
  const trimmed = (keyword ?? '').trim();
  const debounced = useDebouncedValue(trimmed, DEBOUNCE_MS);
  // يُطلب فقط حين يستقرّ ما كُتب: الكلمة المؤخَّرة هي المفتاح، فلا طلب لكل حرف ولا نتيجة لكلمة تغيّرت.
  const ready = enabled && debounced.length >= MIN_KEYWORD_LENGTH;

  const query = useQuery({
    queryKey: queryKeys.searchSuggestions(debounced),
    queryFn: () => api.getSearchSuggestions({ q: debounced }),
    enabled: ready,
    // الاقتراحات لكلمة واحدة لا تتغيّر خلال جلسة كتابة: الرجوع بحرف يعرض القائمة السابقة فوراً بلا وميض.
    staleTime: 60_000,
  });

  return {
    // قائمة قديمة لكلمة أخرى لا تُعرض: الكلمة استقرّت على غيرها، فقائمتها لم تصل بعد.
    items: ready && query.data ? query.data : EMPTY,
    loading: ready && query.isPending,
  };
}
