// ============================================================================
// حساب المخطّطات — منطق خالص مُختبَر، بلا DOM وبلا مكتبة رسم.
//
// لماذا بلا مكتبة؟ قِيس لا افتُرض: المطلوب أربعة أشكال (خطّ، أعمدة، حلقة، شريط نسبة). مكتبة
// جاهزة تضيف ~90 كيلوبايت مضغوطة إلى حزمةٍ أوّلها اليوم 141، وتأتي بافتراضاتها في الاتجاه
// (RTL) والإتاحة والوضع الداكن — وكلّها أشياء يجب أن نتحكّم بها هنا. SVG أربعة ملفّات صغيرة
// نملك سلوكها بالكامل، والحساب هنا مُختبَر وحده بلا متصفّح.
//
// كل دالة تتعامل مع الحالة الفارغة بوضوح: سلسلة بلا نقاط، أو كلّها أصفار (متجر جديد).
// ============================================================================

/** حدود المحور الرأسي. صفر دائماً في الأسفل — منحنى مبيعات لا يبدأ من 900 ليبدو صعوداً. */
export function verticalScale(values, { minTicks = 3 } = {}) {
  const numbers = (values ?? []).filter((v) => Number.isFinite(v));
  const max = numbers.length ? Math.max(...numbers, 0) : 0;
  if (max <= 0) return { max: 1, ticks: [0, 1], allZero: true };

  // حدّ أعلى "جميل": 1، 2، 2.5 أو 5 × قوة عشرة — فتصير علامات المحور أرقاماً يقرؤها إنسان.
  const magnitude = 10 ** Math.floor(Math.log10(max));
  const normalized = max / magnitude;
  const step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10;
  const top = step * magnitude;

  const count = Math.max(minTicks, 4);
  const ticks = Array.from({ length: count + 1 }, (_, i) => (top / count) * i);
  return { max: top, ticks, allZero: false };
}

/**
 * نقاط منحنى خطّي داخل مربّع الرسم.
 * الإحداثي س يُحسب من اليسار دائماً؛ الانعكاس في RTL يقع على عنصر SVG كاملاً (transform)
 * لا هنا — فيبقى الحساب واحداً ويبقى النصّ غير معكوس.
 */
export function linePoints(values, { width, height, max }) {
  const numbers = values ?? [];
  if (numbers.length === 0) return [];
  if (numbers.length === 1) return [{ x: width / 2, y: height - (numbers[0] / max) * height }];

  const step = width / (numbers.length - 1);
  return numbers.map((value, i) => ({
    x: i * step,
    y: height - (Math.max(0, value) / max) * height,
  }));
}

/** مسار SVG من نقاط — خطّ مستقيم بين النقاط: منحنى ناعم يخترع قيماً بين يومين. */
export const linePath = (points) =>
  points.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' ');

/** المسار نفسه مغلقاً إلى القاع — للتظليل تحت الخطّ. */
export const areaPath = (points, height) =>
  points.length === 0 ? '' : `${linePath(points)} L${points.at(-1).x.toFixed(2)},${height} L${points[0].x.toFixed(2)},${height} Z`;

/**
 * شرائح حلقة بالنسب. تتعامل مع ثلاث حالات يخطئ فيها الرسم عادةً:
 *   • المجموع صفر ⇒ لا شرائح (والمكوّن يعرض حالة فارغة، لا حلقة فارغة بلا تفسير).
 *   • شريحة واحدة بنسبة 100% ⇒ قوس كامل لا يُرسم بـ arc (يُعامَل خاصّاً في المكوّن).
 *   • شرائح صفرية تُحذف بدل رسم خطوط بعرض صفر.
 */
export function donutSlices(items) {
  const rows = (items ?? []).filter((item) => Number.isFinite(item.value) && item.value > 0);
  const total = rows.reduce((sum, item) => sum + item.value, 0);
  if (total <= 0) return { total: 0, slices: [] };

  let offset = 0;
  const slices = rows.map((item) => {
    const fraction = item.value / total;
    const slice = { ...item, fraction, percent: Math.round(fraction * 1000) / 10, offset };
    offset += fraction;
    return slice;
  });
  return { total, slices };
}

/** نسبة كل شريط إلى الأكبر — لا إلى المجموع: المقارنة بصرية بين صفوف مرتّبة. */
export function barWidths(values) {
  const numbers = (values ?? []).map((v) => (Number.isFinite(v) ? Math.max(0, v) : 0));
  const max = Math.max(...numbers, 0);
  return numbers.map((value) => (max <= 0 ? 0 : value / max));
}

/**
 * تغيّر بين مدّتين كنسبة مئوية.
 * القسمة على صفر هي الحالة الشائعة (متجر بدأ للتوّ): لا Infinity ولا NaN على الشاشة —
 * نموٌّ من صفر ليس نسبة، بل حقيقة تُقال بكلمات ("أوّل مبيعات").
 */
export function changeRatio(current, previous) {
  const now = Number(current) || 0;
  const before = Number(previous) || 0;
  if (before === 0) return { kind: now > 0 ? 'new' : 'flat', percent: null, direction: now > 0 ? 'up' : 'flat' };
  const ratio = (now - before) / before;
  const percent = Math.round(ratio * 1000) / 10;
  return {
    kind: 'ratio',
    percent: Math.abs(percent),
    direction: percent > 0 ? 'up' : percent < 0 ? 'down' : 'flat',
  };
}
