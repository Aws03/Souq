import { describe, it, expect, beforeEach } from 'vitest';
import { dateLocale, dateOptions, setStoreDateSettings } from './dateLocale';

describe('dateLocale', () => {
  it('اللغة من الزائر والإقليم من المتجر', () => {
    expect(dateLocale('en', 'ar-SA')).toBe('en-SA');
    expect(dateLocale('ar', 'ar-SA')).toBe('ar-SA');
  });

  it('متجر بثقافة بلا إقليم ⇒ اللغة وحدها', () => {
    expect(dateLocale('ar', 'en')).toBe('ar');
    expect(dateLocale('en', '')).toBe('en');
  });

  it('لا يُبقي إقليم الزائر مكان إقليم المتجر', () => {
    // 'en-US' من المتصفّح مع متجر أردني يجب أن يعطي 'en-JO' لا 'en-US'.
    expect(dateLocale('en-US', 'ar-JO')).toBe('en-JO');
  });

  it('بلا شيء إطلاقاً ⇒ قيمة صالحة لا undefined', () => {
    expect(dateLocale(undefined, undefined)).toBe('en');
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
