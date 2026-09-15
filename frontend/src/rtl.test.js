import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';

// ============================================================================
// اتجاه الواجهة (RTL/LTR) ليس ترجمةً للنصوص.
//
// المتجر يُعرض بالعربية من اليمين وبالإنجليزية من اليسار بالبناء نفسه، فأي قاعدة CSS تذكر
// جانباً فيزيائياً (left/right) تكون صحيحة في اتجاه وخاطئة في الآخر — وهذا الصنف من العيوب
// لا يظهر لمن يطوّر بلغة واحدة. أمسك هذا الفحص فعلياً: زرّ كشف كلمة السرّ كان فوق أوّل
// الحروف في الإنجليزية، وشارة عدد السلّة في الزاوية الخاطئة، ومؤشّر القسم الحالي في لوحة
// الإدارة على الحافّة الخارجية.
//
// الاستثناءات بالاسم لا بالصمت: قاعدة تضع left و right معاً (شريط ملتصق بعرض الشاشة) لا
// اتجاه لها، ودرج ينزلق من جانبٍ يحسبه المستدعي من اتجاه الصفحة ويمرّره صراحةً.
// ============================================================================
const styles = Object.keys(import.meta.glob('./**/*.css'));
const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');

// قواعد يُسمح لها بالجانب الفيزيائي، ولكلٍّ سببها.
const ALLOWED = [
  // إسناد متناظر: العنصر ممتدّ على العرض كلّه، فلا "بداية" ولا "نهاية" فيه.
  /left:\s*0;\s*right:\s*0/,
  /right:\s*0;\s*left:\s*0/,
  /top:\s*0;\s*bottom:\s*0;\s*left:\s*0;\s*right:\s*0/,
  // الدرج: الجانب خاصّية يحسبها المستدعي من اتجاه الصفحة (CartDrawer) ويمرّرها.
  /\.panel\.left\s*\{/,
  /\.panel\.right\s*\{/,
];

const PHYSICAL = /(^|[^-\w])(left|right)\s*:|(margin|padding|border)-(left|right)\b|text-align:\s*(left|right)\b/;

describe('اتجاه الواجهة', () => {
  it('يقرأ كل أوراق الأنماط', () => {
    expect(styles.length).toBeGreaterThan(30);
  });

  it('لا قاعدة تعتمد جانباً فيزيائياً خارج الاستثناءات المسمّاة', () => {
    const offenders = [];

    for (const path of styles) {
      for (const [index, line] of read(path).split('\n').entries()) {
        const code = line.split('/*')[0];              // التعليقات تشرح الجوانب ولا تُطبّقها
        if (!PHYSICAL.test(code)) continue;
        if (ALLOWED.some((allowed) => allowed.test(code))) continue;
        offenders.push(`${path}:${index + 1} → ${line.trim().slice(0, 80)}`);
      }
    }

    expect(offenders).toEqual([]);
  });
});
