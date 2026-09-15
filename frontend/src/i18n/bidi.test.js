// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import i18n, { isolateBidi } from './index';
import ar from './locales/ar.json';
import en from './locales/en.json';

// ============================================================================
// اسم يكتبه التاجر قد يكون بعكس اتجاه الجملة التي يدخلها. خوارزمية bidi تحسب اتجاه المقطع
// من محيطه، فتنزلق علامة الترقيم المجاورة إلى الطرف الخطأ: "(كوب حراري)." تُرسم
// ".(كوب حراري)". يراه المستخدم ولا يراه أي اختبار يقارن نصوصاً — فالمحارف موجودة وترتيبها
// المنطقي سليم، والعطب في الرسم وحده.
//
// لذلك يُفحص هنا شيئان: أن العازل يُطبَّق فعلاً عند التوليد، وأن كل موضع يستقبل اسماً من
// بيانات المتجر معلَّم بـ`bidi` في اللغتين — فمفتاحٌ جديد ينسى العلامة يسقط هنا لا عند التاجر.
// ============================================================================
const flatten = (object, prefix = '') =>
  Object.entries(object).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return value && typeof value === 'object' ? flatten(value, path) : [[path, value]];
  });

describe('عزل اتجاه النصّ', () => {
  it('يحيط القيمة بعازلَي يونيكود', () => {
    expect(isolateBidi('كوب حراري')).toBe('⁨كوب حراري⁩');
  });

  it('لا يعزل الفراغ: عازلان حول لا شيء زيادةٌ بلا معنى', () => {
    expect(isolateBidi('')).toBe('');
    expect(isolateBidi(null)).toBe('');
  });

  it('الترجمة نفسها تُخرج الاسم معزولاً', () => {
    const text = i18n.t('admin.business.reason.concentration', { percent: 100, name: 'كوب حراري' });
    expect(text).toContain('⁨كوب حراري⁩');
  });

  it('كل موضع يستقبل اسم تاجر معلَّم في اللغتين', () => {
    const unmarked = [];
    for (const { language, bundle } of [{ language: 'en', bundle: en }, { language: 'ar', bundle: ar }]) {
      for (const [key, value] of flatten(bundle)) {
        if (typeof value === 'string' && /\{\{\s*name\s*\}\}/.test(value)) {
          unmarked.push(`${language}: ${key}`);
        }
      }
    }
    expect(unmarked).toEqual([]);
  });
});
