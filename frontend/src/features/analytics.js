// ============================================================================
// قياسُ سلوك واجهة المتجر من جهة المتصفّح (C9b، ADR-0050 §3).
//
// ثلاثةُ أشياء لا تقع إلّا هنا: أنّ منتجاً **ظهر** في قائمة وفي أيّ موضع، وأنّه **نُقر**، وأنّ
// صفحته **عُوينت**. لا أثر لأيٍّ منها في الخادم، فلولا هذا الملفّ لما وُجدت.
//
// ============================================================================
// ثلاثةُ قرارات تحكم هذا الملفّ، ولكلٍّ منها ضررٌ يمنعه:
//
//   • **لا شيء ينتظر القياس.** كلُّ إرسالٍ غيرُ مُنتظَر وكلُّ خطأٍ مبتلَع. متسوّقٌ يرى خطأً
//     سببه قياس هو أسوأُ نتيجةٍ ممكنة لميزةٍ لا يراها أصلاً.
//   • **الظهورُ يُجمَّع ويُرسَل دفعةً.** تمريرُ صفحةٍ يُنتج عشرات الظهورات في ثوانٍ، وطلبٌ لكلٍّ
//     منها يُغرق الشبكة ويصطدم بحدّ المعدّل — فتضيع البيانات التي جُمعت لأجلها.
//   • **معرّفُ تنفيذ البحث يُحمل في ترويسة كلّ طلب**، لا في حمولة كلّ حدث. هو ما يربط بحثاً
//     بنقرةٍ بسلّةٍ بشراء، والخادمُ يختمه على كلّ ما يُسجَّل في ذلك الطلب — فيعمل حتى لأحداثٍ
//     يكتبها الخادمُ وحده (الإضافة إلى السلّة، الشراء) بلا أن تعرف هذه الشيفرة عنها شيئاً.
// ============================================================================

const ENDPOINT = '/storefront/events';

// الدفعةُ تُرسَل بعد هدوءٍ قصير: تمريرٌ متّصل يُنتج ظهوراً متتابعاً، وانتظارُ لحظةٍ يجمعها في طلب.
const BATCH_DELAY_MS = 800;

let searchExecutionId = null;
let pendingImpressions = new Map();
let pendingListId = null;
let timer = null;
let poster = null;

// يُحقن من `api/client` كي لا يعتمد هذا الملفّ على شكل الطلب — ويُترك فارغاً في الاختبارات
// التي لا تعني بالشبكة.
export function setEventPoster(fn) {
  poster = fn;
}

// ── معرّف تنفيذ البحث ──────────────────────────────────────────────────────
// يُصكّ في الخادم عند بحثٍ بكلمة ويُعاد في جسم النتيجة. نحفظه ونعيده في ترويسة كلّ طلبٍ تالٍ
// حتى بحثٍ جديد — فالسلسلةُ تُقفل بلا أن يحمل كلُّ مكوّنٍ المعرّف بيده.
export function rememberSearchExecution(id) {
  if (typeof id === 'string' && id.length > 0) searchExecutionId = id;
}

export const currentSearchExecution = () => searchExecutionId;

// ── الظهور ────────────────────────────────────────────────────────────────
// المفتاحُ هو المنتج: تمريرٌ ذهاباً وإياباً يُظهر العنصر نفسه مرّتين، والظهورُ الواحد يكفي.
export function reportImpression(listId, productId, position) {
  if (!listId || !productId || !position) return;

  // قائمةٌ جديدة تُرسل ما جُمع قبلها: حدثُ الظهور يحمل قائمةً واحدة بحكم شكله.
  if (pendingListId && pendingListId !== listId) flush();

  pendingListId = listId;
  if (!pendingImpressions.has(productId)) pendingImpressions.set(productId, position);
  schedule();
}

export function reportClick(listId, productId, position) {
  if (!listId || !productId || !position) return;
  // النقرةُ تُرسل فوراً ومعها ما جُمع: الصفحةُ على وشك التبديل، ومؤقّتٌ لن يُستأنف.
  flush({ listId, productId, position });
}

export function reportView(productId, { variantId = null, listId = null, position = null } = {}) {
  if (!productId) return;
  post({ view: { productId, variantId, listId, position } });
}

function schedule() {
  if (timer !== null) return;
  timer = setTimeout(() => { timer = null; flush(); }, BATCH_DELAY_MS);
}

export function flush(click = null) {
  if (timer !== null) { clearTimeout(timer); timer = null; }

  const impressions = [...pendingImpressions].map(([productId, position]) => ({ productId, position }));
  const listId = pendingListId ?? click?.listId ?? null;
  pendingImpressions = new Map();
  pendingListId = null;

  if (impressions.length === 0 && !click) return;

  post({
    listId: click?.listId ?? listId,
    impressions: impressions.length > 0 ? impressions : null,
    click: click ? { productId: click.productId, position: click.position } : null,
  });
}

// الإرسالُ غيرُ مُنتظَر وخطؤه مبتلَع — وهو كلُّ ما يجعل هذا الملفّ آمناً في مسار تصفّح.
function post(body) {
  if (!poster) return;
  try {
    const sent = poster(ENDPOINT, body);
    if (sent && typeof sent.catch === 'function') sent.catch(() => {});
  } catch {
    // قياسٌ لا يُفشل تصفّحاً.
  }
}

// للاختبارات: تُعيد الحالة إلى الصفر بين الحالات.
export function resetAnalytics() {
  if (timer !== null) { clearTimeout(timer); timer = null; }
  pendingImpressions = new Map();
  pendingListId = null;
  searchExecutionId = null;
}
