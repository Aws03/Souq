import { describe, expect, it } from 'vitest';
import { insightOutcome, zeroResultShare } from './insights';

describe('zeroResultShare', () => {
  it('يحسب النسبة من مجموع النافذة', () => {
    expect(zeroResultShare({ totalSearches: 200, zeroResultSearches: 50 })).toBe(25);
  });

  // ============================================================================
  // القسمة على صفر — وهي الحالة التي تُصيب الشاشة يومها الأول: متجرٌ جديد لا بحث فيه.
  // صفرٌ هنا كان سيُطبع "٠٪ من البحوث لم تجد شيئاً" وهي **دعوى مطمئنة عن قياسٍ لم يجرِ**، لا حقيقة.
  // ============================================================================
  it('لا قياس ⇒ null لا صفر', () => {
    expect(zeroResultShare({ totalSearches: 0, zeroResultSearches: 0 })).toBeNull();
    expect(zeroResultShare(undefined)).toBeNull();
    expect(zeroResultShare(null)).toBeNull();
    expect(zeroResultShare({})).toBeNull();
  });

  it('كل البحوث تفشل ⇒ مئة', () => {
    expect(zeroResultShare({ totalSearches: 7, zeroResultSearches: 7 })).toBe(100);
  });
});

describe('insightOutcome', () => {
  it('لم تجد شيئاً أبداً ⇒ خطر', () => {
    expect(insightOutcome({ searches: 5, zeroResultSearches: 5, neverFoundAnything: true }))
      .toEqual({ tone: 'danger', key: 'foundNothing', count: 5 });
  });

  it('تفشل أحياناً ⇒ تحذير بعدد مرّات الفشل', () => {
    expect(insightOutcome({ searches: 10, zeroResultSearches: 3, neverFoundAnything: false }))
      .toEqual({ tone: 'warning', key: 'foundSometimes', count: 3 });
  });

  // ============================================================================
  // الحالة التي كان المكوّن يُخطئ فيها قبل أن يُنقل القرار إلى هنا: كلمةٌ تجد نتائج في كل مرّة كانت
  // تُعرض بنغمة تحذير ونصِّ "فشلت 0 مرّة" — تُخبر التاجر بعملٍ لا وجود له، وبصيغةِ جمعٍ لعددٍ صفر.
  // ============================================================================
  it('تجد نتائج دائماً ⇒ نجاح بلا عدّاد', () => {
    expect(insightOutcome({ searches: 12, zeroResultSearches: 0, neverFoundAnything: false }))
      .toEqual({ tone: 'success', key: 'foundAlways', count: 0 });
  });

  it('حقولٌ ناقصة لا تُسقط الصفّ', () => {
    expect(insightOutcome({})).toEqual({ tone: 'success', key: 'foundAlways', count: 0 });
  });
});
