import { describe, expect, it } from 'vitest';
import ar from './locales/ar.json';
import en from './locales/en.json';

// رموز الأخطاء عقد مع الخادم (ADR-0017): كل رمز مترجم في اللغتين معاً، وإلا ظهر لمستخدم
// إحداهما نصّ الخادم الخام بدل رسالة مفهومة.
describe('error code translations', () => {
  it('exist for the same codes in Arabic and English', () => {
    expect(Object.keys(ar.errors.codes).sort()).toEqual(Object.keys(en.errors.codes).sort());
  });

  it('are never empty', () => {
    for (const messages of [ar.errors.codes, en.errors.codes]) {
      for (const [code, message] of Object.entries(messages)) {
        expect(message.trim(), code).not.toBe('');
      }
    }
  });
});
