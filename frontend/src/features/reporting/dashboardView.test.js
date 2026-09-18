import { describe, it, expect } from 'vitest';
import { hasNoActivity, isBrandNewStore, kpiCards, operationalAlerts, statusSlices, totalOrdersInPeriod, trendBucket, trendPoints } from './dashboardView';

const t = (key) => key;

const dashboard = (overrides = {}) => ({
  currency: 'JOD',
  current: { revenue: 1200, refunds: 200, netRevenue: 1000, orders: 10, averageOrderValue: 120, newCustomers: 4 },
  previous: { revenue: 800, refunds: 0, netRevenue: 800, orders: 8, averageOrderValue: 100, newCustomers: 2 },
  trend: [{ bucket: '2026-03-01T00:00:00Z', revenue: 500, orders: 5 }],
  ordersByStatus: { Pending: 2, Paid: 3, Shipped: 1, Delivered: 4, Cancelled: 1 },
  topProducts: [], topCategories: [],
  inventory: { healthy: 10, low: 2, outOfStock: 1 },
  pendingOrders: 2, pendingRefunds: 1, totalCustomers: 20, repeatCustomers: 5,
  ...overrides,
});

describe('kpiCards', () => {
  it('يعرض صافي الإيراد لا الإيراد الخام، ويحمل مفتاح صيغته', () => {
    // الفرق بينهما هنا 200 مُستردّة — وعرض الخام باسم "الإيراد" يُبالغ بما رُدّ فعلاً.
    const [net] = kpiCards(dashboard());

    expect(net.key).toBe('netRevenue');
    expect(net.value).toBe(1000);
    expect(net.formulaKey).toBe('netRevenue');
  });

  it('يقارن بالمدّة السابقة لا بالصفر', () => {
    const cards = kpiCards(dashboard());
    expect(cards[0].change).toMatchObject({ direction: 'up', percent: 25 });
    expect(cards[1].change).toMatchObject({ direction: 'up', percent: 25 });
  });

  it('لا يحسب رقماً لم يُحسبه الخادم', () => {
    // متوسّط قيمة الطلب يصل محسوباً: حسابه هنا ثانيةً يعني تعريفين يفترقان.
    const data = dashboard({ current: { ...dashboard().current, averageOrderValue: 999 } });
    expect(kpiCards(data)[2].value).toBe(999);
  });

  it('بلا بيانات ⇒ لا بطاقات لا بطاقات بـundefined', () => {
    expect(kpiCards(null)).toEqual([]);
  });
});

describe('operationalAlerts', () => {
  it('يرتّب حسب الإلحاح: النافد والاسترداد قبل المنخفض', () => {
    const keys = operationalAlerts(dashboard()).map((a) => a.key);
    expect(keys).toEqual(['outOfStock', 'pendingRefunds', 'pendingOrders', 'lowStock']);
  });

  it('لا تنبيه بلا رقم', () => {
    const healthy = dashboard({ inventory: { healthy: 5, low: 0, outOfStock: 0 }, pendingOrders: 0, pendingRefunds: 0 });
    expect(operationalAlerts(healthy)).toEqual([]);
  });

  it('كل تنبيه يقود إلى الشاشة التي تحلّه', () => {
    for (const alert of operationalAlerts(dashboard())) {
      expect(alert.href).toMatch(/^\/admin\//);
    }
  });
});

describe('statusSlices', () => {
  it('بترتيب دورة الحياة لا بالأبجدية', () => {
    expect(statusSlices(dashboard().ordersByStatus, t).map((s) => s.label))
      .toEqual(['orders.status.Pending', 'orders.status.Paid', 'orders.status.Shipped',
        'orders.status.Delivered', 'orders.status.Cancelled']);
  });

  it('حالة غائبة تُعرض صفراً لا تُحذف', () => {
    // حذف "لا ملغاة" يجعلها تبدو معلومة غائبة بدل معلومة جيّدة.
    expect(statusSlices({ Paid: 3 }, t).find((s) => s.label.endsWith('Cancelled')).value).toBe(0);
  });

  it('المجموع يشمل الملغاة — هذه حلقة "ماذا جرى"', () => {
    expect(totalOrdersInPeriod(dashboard().ordersByStatus)).toBe(11);
    expect(totalOrdersInPeriod(undefined)).toBe(0);
  });
});

describe('الحالات الفارغة', () => {
  it('متجر بلا نشاط في المدّة يُكتشف', () => {
    const quiet = dashboard({
      current: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      ordersByStatus: { Pending: 0, Paid: 0, Shipped: 0, Delivered: 0, Cancelled: 0 },
    });
    expect(hasNoActivity(quiet)).toBe(true);
    // له عملاء ومخزون: هادئ لا جديد — والفرق يقرّر أي رسالة تُعرض.
    expect(isBrandNewStore(quiet)).toBe(false);
  });

  it('متجر جديد تماماً يُميَّز عن متجر هادئ', () => {
    // الفرق يقرّر الرسالة: إرشاد بدء مقابل "لا مبيعات هذا الأسبوع".
    const fresh = dashboard({
      current: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      ordersByStatus: {}, totalCustomers: 0, inventory: { healthy: 0, low: 0, outOfStock: 0 },
    });
    expect(isBrandNewStore(fresh)).toBe(true);
  });
});

describe('trendPoints', () => {
  it('يختار الحقل المطلوب', () => {
    expect(trendPoints(dashboard(), 'revenue')[0].value).toBe(500);
    expect(trendPoints(dashboard(), 'orders')[0].value).toBe(5);
  });

  it('بلا منحنى ⇒ مصفوفة فارغة لا استثناء', () => {
    expect(trendPoints(null)).toEqual([]);
    expect(trendPoints({ trend: undefined })).toEqual([]);
  });
});

describe('trendBucket', () => {
  const at = (iso, revenue = 0) => ({ bucket: iso, revenue, orders: 0 });

  it('حبّات متتابعة بيوم ⇒ يوم', () => {
    expect(trendBucket({ trend: [at('2026-09-01T00:00:00Z'), at('2026-09-02T00:00:00Z')] })).toBe('day');
  });

  it('حبّات متتابعة بشهر ⇒ شهر — وهذا ما كان التلميح يكذب فيه', () => {
    // Last90Days وThisYear يُجمَعان شهريّاً على الخادم، والتلميح كان يقول "يوميّاً" لهما أيضاً،
    // وهو نفسه ملخّص المخطّط لقارئ الشاشة (M12).
    expect(trendBucket({ trend: [at('2026-07-01T00:00:00Z'), at('2026-08-01T00:00:00Z')] })).toBe('month');
    // فبراير 28 يوماً: أقصر شهر، ويجب أن يُقرأ شهراً لا يوماً.
    expect(trendBucket({ trend: [at('2026-02-01T00:00:00Z'), at('2026-03-01T00:00:00Z')] })).toBe('month');
  });

  it('بلا حبّتين أو بتاريخ غير مقروء ⇒ يوم (الافتراض الأضيق)', () => {
    expect(trendBucket({ trend: [] })).toBe('day');
    expect(trendBucket({ trend: [at('2026-09-01T00:00:00Z')] })).toBe('day');
    expect(trendBucket({ trend: [at('nonsense'), at('also-nonsense')] })).toBe('day');
    expect(trendBucket(null)).toBe('day');
  });
});
