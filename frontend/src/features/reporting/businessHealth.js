import { changeRatio } from './chartScales';

// ============================================================================
// ترجمة أرقام التشغيل إلى فهمٍ تجاريّ — منطق خالص مُختبَر.
//
// الجمهور هنا ليس من يدير المتجر يومياً: مالك، أو شريك، أو مستثمر. لا يعرف معنى "بانتظار
// الدفع" ولا "محجوز من المخزون"، ولا يريد أن يعرف. سؤاله واحد:
//   هل العمل سليم، وينمو، وأين الخطر؟
//
// ثلاث قواعد تحكم هذا الملف:
//
//  1) **لا رقم جديد.** كل ما هنا مشتقّ من استجابة اللوحة نفسها التي يقرؤها المدير. لوحتان
//     بأرقام مختلفة للشيء نفسه تعني أن إحداهما تكذب.
//
//  2) **لا ربح.** المستودع لا يحمل تكلفة شراء، فـ"الهامش" هنا سيكون اختراعاً. الغياب يُعلَن.
//
//  3) **الحكم يُشرَح لا يُطلَق.** "سليم" بلا سببٍ مذكور رأيٌ لا معلومة، ومن يقرأ لا يستطيع
//     أن يختلف معه. كل إشارة تحمل السبب الذي بُنيت عليه.
// ============================================================================

// عتبات الحكم — مجموعة في مكان واحد كي تُراجَع كقرار لا كأرقام مبعثرة في الشيفرة.
const THRESHOLDS = {
  growth: 0.05,          // ±٥٪ حدّ "تغيّر يستحقّ الذكر"؛ ما دونه ضجيج لا اتجاه
  refundShare: 0.10,     // ما استُردّ يتجاوز عُشر الإيراد ⇒ يستحقّ نظرة
  cancelShare: 0.20,     // خُمس الطلبات ملغى ⇒ خلل في المسار لا سوء حظّ
  unpaidShare: 0.25,     // ربع الطلبات لم يُدفع ⇒ الدفع نفسه قد يكون العائق
  concentration: 0.50,   // منتج واحد يزيد عن نصف الإيراد ⇒ تركّز يستحقّ الانتباه
};

/** اتجاه بثلاث قيم فقط: صعود، هبوط، ثبات — والثبات يشمل التغيّر الطفيف. */
export function trendOf(current, previous, threshold = THRESHOLDS.growth) {
  const change = changeRatio(current, previous);
  if (change.kind === 'new') return { ...change, direction: current > 0 ? 'up' : 'flat' };
  if (change.kind === 'flat') return change;
  return change.percent / 100 >= threshold ? change : { ...change, direction: 'flat' };
}

/**
 * مؤشّرات المستوى الأعلى — أربعة لا أربعون.
 * كل واحد بصيغته ومقارنته بالمدّة السابقة المساوية لها طولاً.
 */
export function headlineMetrics(dashboard) {
  if (!dashboard) return [];
  const { current, previous } = dashboard;

  return [
    { key: 'netRevenue', value: current.netRevenue, previous: previous.netRevenue, kind: 'money' },
    { key: 'orders', value: current.orders, previous: previous.orders, kind: 'count' },
    { key: 'averageOrderValue', value: current.averageOrderValue, previous: previous.averageOrderValue, kind: 'money' },
    { key: 'newCustomers', value: current.newCustomers, previous: previous.newCustomers, kind: 'count' },
  ].map((metric) => ({ ...metric, trend: trendOf(metric.value, metric.previous) }));
}

/**
 * نسبة العملاء الذين اشتروا أكثر من مرّة.
 * مقامها كل العملاء لا عملاء المدّة: الولاء صفة تراكمية، ونسبتها إلى أسبوع بلا معنى.
 * بلا عملاء إطلاقاً ⇒ null لا صفر: "لا نعرف بعد" ليست "صفر بالمئة".
 */
export function repeatRate(dashboard) {
  const total = dashboard?.totalCustomers ?? 0;
  if (total <= 0) return null;
  return Math.round((dashboard.repeatCustomers / total) * 1000) / 10;
}

// ============================================================================
// حصّة أكبر منتج من **إيراد المدّة** — مقياس تركّز المخاطر.
//
// **المقام كان مجموع الثمانية الأوائل، لا إيراد المدّة (صُحِّح في M12).** والفرق ليس تجميلاً: متجر
// يبيع عشرين منتجاً كان أكبرُ منتجاته يُقاس على ثمانيةٍ فقط، فتُبالَغ الحصّة دائماً ويُطلق تنبيه
// "التركّز" قبل موعده — والنصّ المعروض يقول "من المبيعات"، والتوثيق يقول "من إيراد المدّة": كلاهما
// يَعِد بما لم يكن يُحسب.
//
// وإيراد المدّة هو المقام الصحيح، مع فارقٍ مقصود: إيراد أسطر المنتج لا يشمل الشحن ولا يخصم الكوبون
// (الخادم يحسبه من أسطر الطلب)، أمّا `revenue` فيشملهما. فالنسبة تصير **أقلّ** من الحقيقة قليلاً لا
// أكثر — وهذا هو الاتجاه الآمن لإشارة خطر: تتأخّر ولا تُطلق باطلاً.
//
// وبقاء مجموع الثمانية بديلاً عند غياب إيراد المدّة ليس تراجعاً: لا مقام آخر، والنسبة حينها من
// الثمانية صراحةً — وهي الحالة التي لا يكون فيها للمدّة إيراد أصلاً.
// ============================================================================
export function topProductShare(dashboard) {
  const products = dashboard?.topProducts ?? [];
  if (products.length === 0) return null;
  const periodRevenue = dashboard?.current?.revenue ?? 0;
  const denominator = periodRevenue > 0
    ? periodRevenue
    : products.reduce((sum, p) => sum + p.revenue, 0);
  if (denominator <= 0) return null;
  // الأكبر لا الأول: الخادم يرتّب تنازليّاً، لكن الاعتماد على ترتيبه ضِمناً هو ما جعل اختباراً
  // يوكّد "الأول" على قائمة غير مرتّبة ويبدو صحيحاً.
  const top = products.reduce((best, p) => (p.revenue > best.revenue ? p : best), products[0]);
  return { name: top.name, share: top.revenue / denominator };
}

/**
 * إشارات الخطر — كلٌّ منها حقيقة قابلة للتحقّق لا انطباع.
 * ترتيبها بالخطورة، وكلٌّ يحمل الأرقام التي بُني عليها كي يُراجَع الحكم لا يُصدَّق.
 */
export function riskSignals(dashboard) {
  if (!dashboard) return [];
  const { current, ordersByStatus } = dashboard;
  const signals = [];

  const placed = Object.values(ordersByStatus ?? {}).reduce((sum, n) => sum + n, 0);

  if (current.revenue > 0 && current.refunds / current.revenue > THRESHOLDS.refundShare) {
    signals.push({
      key: 'refunds', severity: 'high',
      values: { percent: Math.round((current.refunds / current.revenue) * 100) },
    });
  }

  if (placed > 0 && (ordersByStatus.Cancelled ?? 0) / placed > THRESHOLDS.cancelShare) {
    signals.push({
      key: 'cancellations', severity: 'high',
      values: { percent: Math.round(((ordersByStatus.Cancelled ?? 0) / placed) * 100) },
    });
  }

  if (placed > 0 && (ordersByStatus.Pending ?? 0) / placed > THRESHOLDS.unpaidShare) {
    signals.push({
      key: 'unpaid', severity: 'medium',
      values: { percent: Math.round(((ordersByStatus.Pending ?? 0) / placed) * 100) },
    });
  }

  const concentration = topProductShare(dashboard);
  if (concentration && concentration.share > THRESHOLDS.concentration) {
    signals.push({
      key: 'concentration', severity: 'medium',
      values: { percent: Math.round(concentration.share * 100), name: concentration.name },
    });
  }

  if ((dashboard.inventory?.outOfStock ?? 0) > 0) {
    signals.push({ key: 'outOfStock', severity: 'medium', values: { count: dashboard.inventory.outOfStock } });
  }

  return signals;
}

/**
 * حكم واحد على الصحّة العامّة، مع سببه.
 * لا يُطلَق حكم دون سبب مذكور: قارئٌ يرى "يحتاج انتباهاً" بلا تفسير لا يستطيع فعل شيء به.
 */
export function businessHealth(dashboard) {
  if (!dashboard) return { level: 'unknown', reasonKey: 'noData' };

  const noHistory = dashboard.current.orders === 0 && dashboard.previous.orders === 0;
  if (noHistory) return { level: 'unknown', reasonKey: 'noSales' };

  const risks = riskSignals(dashboard);
  if (risks.some((r) => r.severity === 'high')) return { level: 'attention', reasonKey: risks[0].key, values: risks[0].values };

  const revenue = trendOf(dashboard.current.netRevenue, dashboard.previous.netRevenue);
  if (revenue.direction === 'down') return { level: 'watch', reasonKey: 'revenueDown', values: { percent: revenue.percent } };
  if (risks.length > 0) return { level: 'watch', reasonKey: risks[0].key, values: risks[0].values };
  if (revenue.direction === 'up') return { level: 'healthy', reasonKey: 'revenueUp', values: { percent: revenue.percent } };
  return { level: 'healthy', reasonKey: 'steady' };
}
