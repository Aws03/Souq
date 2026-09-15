// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { oppositeMode, readStoredMode, resolveThemeMode, writeStoredMode, clearStoredMode } from './themeMode';

// ============================================================================
// ترتيب مصادر الوضع هو القرار كلّه: من يغلب من، ومتى.
// واختيار الزائر يجب ألّا يُنقَض بتفضيل المتجر — وإلا صار الزرّ زينة.
// ============================================================================
describe('resolveThemeMode', () => {
  it('اختيار الزائر يغلب تفضيل المتجر ونظامه', () => {
    expect(resolveThemeMode({ stored: 'light', storePreference: 'dark', systemPrefersDark: true })).toBe('light');
    expect(resolveThemeMode({ stored: 'dark', storePreference: 'light', systemPrefersDark: false })).toBe('dark');
  });

  it('بلا اختيار زائر: تفضيل المتجر يغلب نظام الزائر', () => {
    expect(resolveThemeMode({ storePreference: 'dark', systemPrefersDark: false })).toBe('dark');
    expect(resolveThemeMode({ storePreference: 'light', systemPrefersDark: true })).toBe('light');
  });

  it('"system" من المتجر تعني: اسأل المتصفّح', () => {
    expect(resolveThemeMode({ storePreference: 'system', systemPrefersDark: true })).toBe('dark');
    expect(resolveThemeMode({ storePreference: 'system', systemPrefersDark: false })).toBe('light');
  });

  it('بلا شيء إطلاقاً ⇒ فاتح، لا undefined', () => {
    expect(resolveThemeMode()).toBe('light');
    expect(resolveThemeMode({})).toBe('light');
  });

  it('قيمة محفوظة تالفة تُتجاهَل بدل أن تُطبَّق', () => {
    // مفتاح عبث به أحد في المتصفّح يجب ألّا يضع الصفحة في وضع لا وجود له.
    expect(resolveThemeMode({ stored: 'neon', storePreference: 'dark' })).toBe('dark');
    expect(resolveThemeMode({ stored: '', systemPrefersDark: true })).toBe('dark');
  });
});

describe('oppositeMode', () => {
  it('يبدّل بين الوضعين', () => {
    expect(oppositeMode('dark')).toBe('light');
    expect(oppositeMode('light')).toBe('dark');
  });
});

describe('التخزين', () => {
  beforeEach(() => { clearStoredMode(); vi.unstubAllGlobals(); });

  it('يحفظ الوضع الصالح ويقرؤه', () => {
    writeStoredMode('dark');
    expect(readStoredMode()).toBe('dark');
  });

  it('لا يحفظ قيمة غير صالحة', () => {
    writeStoredMode('rainbow');
    expect(readStoredMode()).toBeNull();
  });

  it('تخزين محجوب لا يُسقط الصفحة', () => {
    // نافذة خاصة أو إعدادات صارمة: القراءة والكتابة ترميان.
    vi.stubGlobal('localStorage', {
      getItem: () => { throw new Error('blocked'); },
      setItem: () => { throw new Error('blocked'); },
      removeItem: () => { throw new Error('blocked'); },
    });

    expect(() => writeStoredMode('dark')).not.toThrow();
    expect(readStoredMode()).toBeNull();
  });
});
