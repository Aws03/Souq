import { describe, it, expect } from 'vitest';
import { businessHealth, headlineMetrics, repeatRate, riskSignals, topProductShare, trendOf } from './businessHealth';

const data = (overrides = {}) => ({
  current: { revenue: 1000, refunds: 0, netRevenue: 1000, orders: 10, averageOrderValue: 100, newCustomers: 5 },
  previous: { revenue: 800, refunds: 0, netRevenue: 800, orders: 8, averageOrderValue: 100, newCustomers: 4 },
  ordersByStatus: { Pending: 1, Paid: 4, Shipped: 2, Delivered: 3, Cancelled: 0 },
  topProducts: [{ productId: 1, name: 'A', revenue: 300 }, { productId: 2, name: 'B', revenue: 700 }],
  inventory: { healthy: 10, low: 0, outOfStock: 0 },
  totalCustomers: 40, repeatCustomers: 10,
  ...overrides,
});

describe('trendOf', () => {
  it('تغيّر طفيف ثبات لا اتجاه', () => {
    // ٢٪ ضجيج لا نموّ؛ تسميته "نموّاً" تجعل كل أسبوع قصّة.
    expect(trendOf(102, 100).direction).toBe('flat');
    expect(trendOf(120, 100).direction).toBe('up');
  });

  it('النموّ من صفر ليس نسبة', () => {
    expect(trendOf(500, 0)).toMatchObject({ kind: 'new', percent: null, direction: 'up' });
  });
});

describe('headlineMetrics', () => {
  it('أربعة مؤشّرات لا أربعون، وصافي الإيراد أوّلها', () => {
    const metrics = headlineMetrics(data());
    expect(metrics).toHaveLength(4);
    expect(metrics[0].key).toBe('netRevenue');
  });

  it('لا يحسب رقماً جديداً — كلّها من الاستجابة نفسها', () => {
    const metrics = headlineMetrics(data({ current: { ...data().current, averageOrderValue: 77 } }));
    expect(metrics.find((m) => m.key === 'averageOrderValue').value).toBe(77);
  });

  it('بلا بيانات ⇒ لا مؤشّرات', () => {
    expect(headlineMetrics(null)).toEqual([]);
  });
});

describe('repeatRate', () => {
  it('ينسب العملاء المُعيدين إلى كل العملاء لا إلى عملاء المدّة', () => {
    expect(repeatRate(data())).toBe(25);
  });

  it('بلا عملاء ⇒ "لا نعرف بعد" لا صفر بالمئة', () => {
    // صفر بالمئة حكمٌ على ولاءٍ لم يُقَس بعد.
    expect(repeatRate(data({ totalCustomers: 0 }))).toBeNull();
  });
});

describe('topProductShare', () => {
  it('يعطي حصّة الأعلى إيراداً', () => {
    expect(topProductShare(data()).share).toBeCloseTo(0.3);
  });

  it('بلا منتجات ⇒ null', () => {
    expect(topProductShare(data({ topProducts: [] }))).toBeNull();
  });
});

describe('riskSignals', () => {
  it('استرداد مرتفع إشارة خطر عالية', () => {
    const risks = riskSignals(data({ current: { ...data().current, revenue: 1000, refunds: 300 } }));
    expect(risks[0]).toMatchObject({ key: 'refunds', severity: 'high' });
    expect(risks[0].values.percent).toBe(30);
  });

  it('إلغاءات كثيرة خللٌ في المسار', () => {
    const risks = riskSignals(data({ ordersByStatus: { Pending: 0, Paid: 2, Shipped: 0, Delivered: 0, Cancelled: 8 } }));
    expect(risks.map((r) => r.key)).toContain('cancellations');
  });

  it('تركّز الإيراد في منتج واحد يُرفع كخطر', () => {
    const risks = riskSignals(data({ topProducts: [{ productId: 1, name: 'A', revenue: 900 }, { productId: 2, name: 'B', revenue: 100 }] }));
    const concentration = risks.find((r) => r.key === 'concentration');
    expect(concentration.values).toMatchObject({ percent: 90, name: 'A' });
  });

  it('كل إشارة تحمل أرقامها كي يُراجَع الحكم لا يُصدَّق', () => {
    for (const risk of riskSignals(data({ current: { ...data().current, refunds: 400 } }))) {
      expect(risk.values).toBeTruthy();
    }
  });

  it('متجر سليم ⇒ لا إشارات', () => {
    expect(riskSignals(data())).toEqual([]);
  });

  it('لا قسمة على صفر في متجر بلا طلبات', () => {
    const empty = data({
      current: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      ordersByStatus: {}, topProducts: [],
    });
    expect(() => riskSignals(empty)).not.toThrow();
    expect(riskSignals(empty)).toEqual([]);
  });
});

describe('businessHealth', () => {
  it('لا يُطلق حكماً بلا سبب', () => {
    for (const input of [data(), data({ current: { ...data().current, refunds: 500 } }), null]) {
      expect(businessHealth(input).reasonKey).toBeTruthy();
    }
  });

  it('نموّ بلا مخاطر ⇒ سليم', () => {
    expect(businessHealth(data())).toMatchObject({ level: 'healthy', reasonKey: 'revenueUp' });
  });

  it('خطر عالٍ يغلب النموّ', () => {
    // إيراد صاعد مع ثلث مُستردّ ليس صحّة.
    const risky = data({ current: { revenue: 1000, refunds: 350, netRevenue: 650, orders: 10, averageOrderValue: 100, newCustomers: 5 } });
    expect(businessHealth(risky)).toMatchObject({ level: 'attention', reasonKey: 'refunds' });
  });

  it('انكماش الإيراد ⇒ مراقبة بسببها', () => {
    const shrinking = data({ current: { ...data().current, netRevenue: 500 } });
    expect(businessHealth(shrinking)).toMatchObject({ level: 'watch', reasonKey: 'revenueDown' });
  });

  it('متجر بلا تاريخ مبيعات ⇒ "غير معروف" لا "سليم"', () => {
    // الحكم بالصحّة على متجر لم يبع شيئاً كذبة مريحة.
    const fresh = data({
      current: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      previous: { revenue: 0, refunds: 0, netRevenue: 0, orders: 0, averageOrderValue: 0, newCustomers: 0 },
      ordersByStatus: {}, topProducts: [],
    });
    expect(businessHealth(fresh)).toMatchObject({ level: 'unknown', reasonKey: 'noSales' });
  });
});
