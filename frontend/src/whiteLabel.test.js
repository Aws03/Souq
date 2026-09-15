import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// ============================================================================
// لا اسم علامة ولا عملة ولا بيانات تواصل متجر بعينه في شيفرة الواجهة (المرحلة 15، WhiteLabel.md §6، A4/A5): كل ذلك من إعداد
// المتجر في وقت التشغيل (TenantProvider). يفحص كل ملفات المصدر والترجمات والأنماط وindex.html — ملف جديد بعلامة أو عملة مكتوبة
// يُفشله. ملفات الاختبار مستثناة (بيانات تجريبية).
// الـ glob (غير المحمَّل) يعدّد المسارات فقط، والنصّ يُقرأ خاماً من القرص — لا تحويل Vite (وحدات CSS/JSON) بين الملف والفحص.
// ============================================================================
const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const sources = Object.fromEntries(
  Object.keys(import.meta.glob(['./**/*.{js,jsx,css,json}', '!./**/*.test.js', '!./**/*.test.jsx']))
    .map((path) => [path, read(path)]),
);
const indexHtml = read('../index.html');

const FORBIDDEN = [
  { name: 'store brand', pattern: /\bmarka\b|ماركة/i },
  { name: 'hard-coded currency', pattern: /\bJOD\b|د\.أ|دينار/ },
  { name: 'store contact detail', pattern: /\+962|marka\.example/i },

  // أُضيفت في المرحلة 16 بعد إصلاح ما كانت تمسكه (TD-27): موضع تاريخ مكتوب حرفياً
  // ('ar-JO' لكل متجر)، ودولة مفترضة في نموذج العنوان. كلاهما يجعل متجراً يرث عُرف متجر آخر.
  { name: 'hard-coded date locale', pattern: /['"][a-z]{2}-[A-Z]{2}['"]/, except: ['./app/dateLocale.js'] },
  { name: 'assumed country', pattern: /country:\s*['"][A-Za-z]{2}['"]/ },
];

describe('white-label source check', () => {
  it('reads the whole frontend source', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(150);
  });

  it.each(FORBIDDEN)('contains no $name literal', ({ pattern, except = [] }) => {
    // الاستثناء بالاسم لا بالصمت: الملفّ الوحيد المسموح له بذكر مواضع التواريخ هو الذي يشرح
    // قاعدتها، وذكره هناك في تعليق. أي ملفّ آخر يذكرها يفشل.
    const offenders = Object.entries({ ...sources, '../index.html': indexHtml })
      .filter(([file]) => !except.includes(file))
      .filter(([, text]) => pattern.test(text))
      .map(([file]) => file);
    expect(offenders).toEqual([]);
  });
});
