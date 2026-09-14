import { describe, expect, it } from 'vitest';
import { HOME, safeRedirect } from './safeRedirect';

// أحرف التحكّم تُبنى برموزها لا تُكتب حرفيّاً: حرف غير مرئي في ملف اختبار يُنسخ ويُقصّ بلا أن يلاحظ أحد اختفاءه.
const control = (code) => String.fromCharCode(code);

describe('safeRedirect', () => {
  it('keeps an ordinary internal path, with its query and hash', () => {
    expect(safeRedirect('/orders/5')).toBe('/orders/5');
    expect(safeRedirect('/products?page=2&sort=PriceAsc')).toBe('/products?page=2&sort=PriceAsc');
    expect(safeRedirect('/account#addresses')).toBe('/account#addresses');
  });

  // التحويل يقع بعد نجاح المصادقة مباشرةً، فالوجهة الخارجية هنا أقنع صور التصيّد.
  it('refuses anything the browser would read as another origin', () => {
    expect(safeRedirect('//evil.example')).toBe(HOME);
    expect(safeRedirect('/\\evil.example')).toBe(HOME);        // الشرطة العكسية: GHSA-wrjc-x8rr-h8h6
    expect(safeRedirect('https://evil.example')).toBe(HOME);
    expect(safeRedirect('http://evil.example')).toBe(HOME);
    expect(safeRedirect('javascript:alert(1)')).toBe(HOME);
  });

  // المتصفّح يتجاهل هذه الأحرف في أول العنوان، فالفحص قبل تنظيفها كان يُخدع بها.
  it('strips leading whitespace and C0 control characters before judging', () => {
    expect(safeRedirect('  //evil.example')).toBe(HOME);
    expect(safeRedirect('\t/\\evil.example')).toBe(HOME);
    expect(safeRedirect(control(1) + '//evil.example')).toBe(HOME);
    expect(safeRedirect(control(31) + '/\\evil.example')).toBe(HOME);
    expect(safeRedirect('\n/orders/5')).toBe('/orders/5');
  });

  it('falls back for a missing, empty or login destination', () => {
    expect(safeRedirect(undefined)).toBe(HOME);
    expect(safeRedirect(null)).toBe(HOME);
    expect(safeRedirect('')).toBe(HOME);
    expect(safeRedirect('/login')).toBe(HOME);
  });

  it('honours an explicit fallback', () => {
    expect(safeRedirect('//evil.example', '/admin')).toBe('/admin');
    expect(safeRedirect(undefined, '/admin')).toBe('/admin');
  });
});
