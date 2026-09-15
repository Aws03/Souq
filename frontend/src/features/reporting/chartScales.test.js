import { describe, it, expect } from 'vitest';
import { areaPath, barWidths, changeRatio, donutSlices, linePath, linePoints, verticalScale } from './chartScales';

// ============================================================================
// حساب المخطّطات. أهمّ ما يُختبر هنا هو الحالات التي تجعل لوحةً تعرض NaN أو ∞ أو منحنى كاذب:
// متجر بلا مبيعات، ونموّ من صفر، ومحور لا يبدأ من صفر.
// ============================================================================
describe('verticalScale', () => {
  it('يبدأ من صفر دائماً — لا محور يبدأ من 900 ليبدو الركود صعوداً', () => {
    const scale = verticalScale([950, 980, 1000]);
    expect(scale.ticks[0]).toBe(0);
  });

  it('يعطي حدّاً أعلى يقرؤه إنسان', () => {
    expect(verticalScale([37]).max).toBe(50);
    expect(verticalScale([230]).max).toBe(250);
    expect(verticalScale([1400]).max).toBe(2000);
  });

  it('كل القيم أصفار ⇒ مقياس صالح ومُعلَن', () => {
    // متجر جديد: محور بلا حدّ أعلى يعني قسمة على صفر في كل نقطة.
    const scale = verticalScale([0, 0, 0]);
    expect(scale.allZero).toBe(true);
    expect(scale.max).toBe(1);
  });

  it('سلسلة فارغة لا تُسقط الحساب', () => {
    expect(verticalScale([]).allZero).toBe(true);
    expect(verticalScale(undefined).max).toBe(1);
  });

  it('يتجاهل القيم غير الرقمية بدل إنتاج NaN', () => {
    expect(verticalScale([10, null, undefined, NaN, 20]).max).toBe(20);
  });
});

describe('linePoints', () => {
  it('يوزّع النقاط على العرض ويقلب المحور الرأسي', () => {
    const points = linePoints([0, 50, 100], { width: 200, height: 100, max: 100 });

    expect(points.map((p) => p.x)).toEqual([0, 100, 200]);
    expect(points[0].y).toBe(100);   // صفر في القاع
    expect(points[2].y).toBe(0);     // الأعلى في القمّة
  });

  it('نقطة واحدة تُوضع في المنتصف لا على الحافّة', () => {
    expect(linePoints([5], { width: 200, height: 100, max: 10 })[0].x).toBe(100);
  });

  it('بلا نقاط ⇒ لا مسار', () => {
    expect(linePoints([], { width: 200, height: 100, max: 1 })).toEqual([]);
    expect(linePath([])).toBe('');
    expect(areaPath([], 100)).toBe('');
  });

  it('القيم السالبة تُقصّ عند القاع لا تخرج من الإطار', () => {
    const points = linePoints([-40, 10], { width: 100, height: 100, max: 10 });
    expect(points[0].y).toBe(100);
  });
});

describe('donutSlices', () => {
  it('يحوّل القيم إلى نسب متتابعة', () => {
    const { total, slices } = donutSlices([{ value: 30 }, { value: 10 }]);

    expect(total).toBe(40);
    expect(slices[0].percent).toBe(75);
    expect(slices[1].offset).toBeCloseTo(0.75);
  });

  it('يحذف الشرائح الصفرية بدل رسم خطوط بلا عرض', () => {
    expect(donutSlices([{ value: 5 }, { value: 0 }]).slices).toHaveLength(1);
  });

  it('مجموع صفر ⇒ لا شرائح (والمكوّن يعرض حالة فارغة)', () => {
    expect(donutSlices([{ value: 0 }]).total).toBe(0);
    expect(donutSlices([]).slices).toEqual([]);
    expect(donutSlices(undefined).slices).toEqual([]);
  });
});

describe('barWidths', () => {
  it('ينسب كل شريط إلى الأكبر لا إلى المجموع', () => {
    expect(barWidths([10, 5, 0])).toEqual([1, 0.5, 0]);
  });

  it('كلّها أصفار ⇒ أصفار لا NaN', () => {
    expect(barWidths([0, 0])).toEqual([0, 0]);
    expect(barWidths([])).toEqual([]);
  });
});

describe('changeRatio', () => {
  it('يحسب النموّ والانكماش', () => {
    expect(changeRatio(150, 100)).toMatchObject({ kind: 'ratio', percent: 50, direction: 'up' });
    expect(changeRatio(80, 100)).toMatchObject({ percent: 20, direction: 'down' });
  });

  it('النموّ من صفر ليس نسبة — لا ∞ ولا NaN على الشاشة', () => {
    // الحالة الشائعة في متجر بدأ للتوّ: أوّل مبيعات مقابل أسبوع فارغ.
    expect(changeRatio(500, 0)).toMatchObject({ kind: 'new', percent: null, direction: 'up' });
  });

  it('صفر مقابل صفر ثبات لا نموّ', () => {
    expect(changeRatio(0, 0)).toMatchObject({ kind: 'flat', direction: 'flat' });
  });

  it('قيم غائبة تُعامَل كأصفار', () => {
    expect(changeRatio(undefined, null).direction).toBe('flat');
  });
});
