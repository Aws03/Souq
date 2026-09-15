import { describe, it, expect, beforeEach } from 'vitest';
import { dateLocale, dateOptions, setStoreDateSettings } from './dateLocale';

describe('dateLocale', () => {
  it('اللغة من الزائر والإقليم من المتجر', () => {
    expect(dateLocale('en', 'ar-SA')).toBe('en-SA-u-nu-latn');
    expect(dateLocale('ar', 'ar-SA')).toBe('ar-SA-u-nu-latn');
  });

  it('متجر بثقافة بلا إقليم ⇒ اللغة وحدها', () => {
    expect(dateLocale('ar', 'en')).toBe('ar-u-nu-latn');
    expect(dateLocale('en', '')).toBe('en-u-nu-latn');
  });

  it('لا يُبقي إقليم الزائر مكان إقليم المتجر', () => {
    // 'en-US' من المتصفّح مع متجر أردني يجب أن يعطي 'en-JO' لا 'en-US'.
    expect(dateLocale('en-US', 'ar-JO')).toBe('en-JO-u-nu-latn');
  });

  it('بلا شيء إطلاقاً ⇒ قيمة صالحة لا undefined', () => {
    expect(dateLocale(undefined, undefined)).toBe('en-u-nu-latn');
  });

  it('أرقام لاتينية دائماً — كما يفعل منسّق المال', () => {
    // الصفحة الواحدة كانت تعرض السعر "4" والتاريخ "٤".
    const formatted = new Date('2026-03-04T10:00:00Z').toLocaleDateString(dateLocale('ar', 'ar-JO'));
    expect(formatted).toMatch(/[0-9]/);
    expect(formatted).not.toMatch(/[\u0660-\u0669]/);
  });
});

describe('dateOptions', () => {
  beforeEach(() => setStoreDateSettings({}));

  it('يضيف منطقة المتجر الزمنية', () => {
    setStoreDateSettings({ timeZone: 'Asia/Amman' });
    expect(dateOptions({ dateStyle: 'medium' })).toEqual({ dateStyle: 'medium', timeZone: 'Asia/Amman' });
  });

  it('بلا منطقة ⇒ الخيارات كما هي (منطقة المتصفّح)', () => {
    expect(dateOptions({ dateStyle: 'medium' })).toEqual({ dateStyle: 'medium' });
  });

  it('منطقة غير صالحة تُتجاهَل بدل أن تُسقط عرض التاريخ', () => {
    setStoreDateSettings({ timeZone: 'Not/AZone' });
    expect(dateOptions({ dateStyle: 'medium' })).toEqual({ dateStyle: 'medium' });
  });
});
