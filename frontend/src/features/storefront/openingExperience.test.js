// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  hasSeenOpening, isOpeningEntryPath, markOpeningSeen, resolveOpeningStyle, shouldPlayOpening,
} from './openingExperience';

// ============================================================================
// تجربة الافتتاح. الاختبار هنا عن *متى لا تُعرض* أكثر منه عن متى تُعرض: كل شرط منع له سبب،
// وسقوط أيٍّ منها يحوّل لمسةً مميّزة إلى عائق أمام كل زائر.
// ============================================================================
const base = {
  enabled: true, pathname: '/', seenThisSession: false, prefersReducedMotion: false, userAgent: 'Mozilla/5.0',
};

beforeEach(() => { try { sessionStorage.clear(); } catch { /* محجوب */ } });

describe('shouldPlayOpening', () => {
  it('تُعرض لزائر أوّل مرّة على الواجهة في متجر فعّلها', () => {
    expect(shouldPlayOpening(base)).toBe(true);
  });

  it('متجر لم يفعّلها ⇒ لا شيء (والافتراضي هو عدم التفعيل)', () => {
    expect(shouldPlayOpening({ ...base, enabled: false })).toBe(false);
    expect(shouldPlayOpening({})).toBe(false);
    expect(shouldPlayOpening()).toBe(false);
  });

  it('تقليل الحركة ⇒ لا كشف إطلاقاً، لا نسخة أخفّ', () => {
    // الحركة نفسها هي ما يُطلب تقليله؛ ومعنى الكشف يصل من الصفحة التالية فوراً.
    expect(shouldPlayOpening({ ...base, prefersReducedMotion: true })).toBe(false);
  });

  it('لا تتكرّر في الجلسة نفسها', () => {
    // تحفة أوّل مرّة تصير ضريبةً في الخامسة.
    expect(shouldPlayOpening({ ...base, seenThisSession: true })).toBe(false);
  });

  it('رابط عميق يفتح ما طُلب لا ستارة أمامه', () => {
    for (const pathname of ['/products/blue-shirt', '/cart', '/checkout', '/offers']) {
      expect(shouldPlayOpening({ ...base, pathname })).toBe(false);
    }
  });

  it('زاحف البحث لا يرى كشفاً', () => {
    for (const userAgent of ['Googlebot/2.1', 'Mozilla/5.0 (compatible; bingbot/2.0)', 'HeadlessChrome/120']) {
      expect(shouldPlayOpening({ ...base, userAgent })).toBe(false);
    }
  });

  it('تقليل الحركة يغلب كل شيء آخر', () => {
    expect(shouldPlayOpening({ ...base, prefersReducedMotion: true, seenThisSession: false })).toBe(false);
  });
});

describe('isOpeningEntryPath', () => {
  it('الواجهة وحدها', () => {
    expect(isOpeningEntryPath('/')).toBe(true);
    expect(isOpeningEntryPath('')).toBe(true);
    expect(isOpeningEntryPath('/offers')).toBe(false);
  });
});

describe('ذاكرة الجلسة', () => {
  it('تُعلَّم كمرئية وتُقرأ كذلك', () => {
    expect(hasSeenOpening()).toBe(false);
    markOpeningSeen();
    expect(hasSeenOpening()).toBe(true);
  });

  it('تخزين محجوب ⇒ تُعتبر مرئية بدل أن تتكرّر عند كل تنقّل', () => {
    // الاتجاه الآمن هنا هو "لا تعرض": التكرار أسوأ من الغياب.
    vi.stubGlobal('sessionStorage', {
      getItem: () => { throw new Error('blocked'); },
      setItem: () => { throw new Error('blocked'); },
      clear: () => {},
    });

    expect(hasSeenOpening()).toBe(true);
    expect(() => markOpeningSeen()).not.toThrow();
    vi.unstubAllGlobals();
  });
});

describe('resolveOpeningStyle', () => {
  it('نمط غير معروف يسقط إلى المعروف لا إلى شاشة سوداء', () => {
    expect(resolveOpeningStyle('doors')).toBe('doors');
    expect(resolveOpeningStyle('fireworks')).toBe('doors');
    expect(resolveOpeningStyle(undefined)).toBe('doors');
  });
});
