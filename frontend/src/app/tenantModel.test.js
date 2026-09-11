import { describe, expect, it } from 'vitest';
import {
  bootOutcome, contrastRatio, currencyDecimals, documentTitle, fontStylesheetUrl, formatMoney, isModuleEnabled,
  mutedText, pickText, readableOn, storeName, supportedLanguage, themeVariables,
} from './tenantModel';

// متجران بهويّتين وعملتين ووحدات مختلفة — البناء نفسه يرسم كلاً منهما بما في إعداده (معيار خروج المرحلة 15).
const storeA = {
  name: 'Store A',
  modules: ['promotions', 'reviews', 'wishlist'],
  settings: {
    displayName: { ar: 'متجر أ', en: 'Store A' },
    locale: { defaultCulture: 'ar', enabledCultures: ['ar', 'en'], currency: 'KWD', currencyDecimals: 3 },
    branding: { colors: { primary: '#0F3B3A', accent: '#E8A33D', background: '#FAF7F1', text: '#1A2421', onPrimary: '#FFFFFF' }, typography: 'kufi-tajawal' },
    seo: { title: { ar: 'متجر أ — الرئيسية' }, description: {} },
  },
};
const storeB = {
  name: 'Store B',
  modules: ['reviews'],
  settings: {
    displayName: { en: 'Store B' },
    locale: { defaultCulture: 'en', enabledCultures: ['en'], currency: 'USD', currencyDecimals: 2 },
    branding: { colors: { primary: '#12355B', accent: '#00B4D8', background: '#FFFFFF', text: '#0B1320' }, typography: 'ibm-plex' },
    seo: { title: {}, description: {} },
  },
};

describe('tenant runtime', () => {
  it('derives different semantic tokens and fonts from each store', () => {
    const a = themeVariables(storeA.settings.branding);
    const b = themeVariables(storeB.settings.branding);

    expect(a['--color-primary']).toBe('#0F3B3A');
    expect(b['--color-primary']).toBe('#12355B');
    expect(a['--tenant-font-heading']).toContain('Reem Kufi');
    expect(b['--tenant-font-body']).toContain('IBM Plex Sans Arabic');
    expect(a['--color-on-primary']).toBe('#FFFFFF');
    expect(fontStylesheetUrl('cairo')).toContain('family=Cairo');
  });

  it('keeps derived colours readable and falls back to neutral values for missing ones', () => {
    for (const { colors } of [storeA.settings.branding, storeB.settings.branding]) {
      const vars = themeVariables({ colors });
      expect(contrastRatio(vars['--color-text-muted'], vars['--color-bg'])).toBeGreaterThanOrEqual(4.5);
    }
    expect(themeVariables({ colors: { primary: 'red' } })['--color-primary']).toMatch(/^#[0-9A-F]{6}$/);
    expect(readableOn('#FFE066')).toBe('#111827');
    expect(readableOn('#12355B')).toBe('#FFFFFF');
    expect(mutedText('#1A2421', '#FAF7F1')).not.toBe('#1A2421');
  });

  it('formats money in each store currency with its minor units', () => {
    expect(currencyDecimals('KWD')).toBe(3);
    expect(currencyDecimals('USD')).toBe(2);
    // Intl يفصل رمز العملة عن المبلغ بمسافة غير منكسرة (U+00A0) — لا ينقسم السعر على سطرين.
    expect(formatMoney(12.5, 'KWD', 'en')).toBe('KWD\u00a012.500');
    expect(formatMoney(12.5, 'USD', 'en')).toBe('USD\u00a012.50');
    expect(formatMoney(12.5, 'KWD', 'ar')).toContain('12.500');
    expect(formatMoney(12.5, '', 'ar')).toBe('12.50');
  });

  it('shows each store its own modules, name and title in the visitor language', () => {
    expect(isModuleEnabled(storeA, 'wishlist')).toBe(true);
    expect(isModuleEnabled(storeB, 'wishlist')).toBe(false);
    expect(isModuleEnabled(null, 'reviews')).toBe(false);
    expect(storeName(storeA, 'en')).toBe('Store A');
    expect(storeName(storeB, 'ar')).toBe('Store B');
    expect(documentTitle(storeA, 'ar')).toBe('متجر أ — الرئيسية');
    expect(documentTitle(storeB, 'en')).toBe('Store B');
    expect(pickText({ en: '', ar: 'نصّ' }, 'en', 'ar')).toBe('نصّ');
  });

  it('keeps the visitor language only when the store enables it', () => {
    expect(supportedLanguage(storeA, 'en')).toBe('en');
    expect(supportedLanguage(storeB, 'ar')).toBe('en');
  });

  it('maps the boot response to the right screen', () => {
    expect(bootOutcome({ status: 503, code: 'StoreUnavailable' })).toBe('closed');
    expect(bootOutcome({ status: 404, code: 'StoreNotFound' })).toBe('unknown');
    expect(bootOutcome({ status: 404, code: 'NotFound' })).toBe('platform');
    expect(bootOutcome(new TypeError('Failed to fetch'))).toBe('error');
  });
});
