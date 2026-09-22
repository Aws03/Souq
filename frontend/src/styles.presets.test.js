import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { THEME_PRESETS, themeVariables } from './app/tenantModel';

// ============================================================================
// القوالبُ الثلاثة (C8، TD-65) — **واتّفاقُ نصفَيهما**.
//
// التعريفُ مقسومٌ بين موضعين لسببٍ مكتوب: الظلُّ في الوضع الفاتح مصبوغٌ بلون هوية المتجر فيُشتقّ
// في `tenantModel` ويُكتب سطرياً، وبقيّةُ القالب رموزُ شكلٍ في `styles.css`. والقسمةُ مقبولةٌ ما
// دام النصفان يقولان الشيء نفسه — **وهذا بالضبط ما لا يبقى صحيحاً وحده**: `styles.darkTokens`
// موجودٌ لأنّ قسمةً مشابهة انفرطت مرّةً وتُركت سنةً بلا أن يُمسكها شيء.
//
// وما يُفحص هنا ليس القيم بأعيانها بل **ما تعنيه**: أنّ لكلّ قالبٍ قاعدةً في الورقة، وأنّ
// القالبين غير الكلاسيكيّين يستبدلان بالظلّ حلقةً في الموضعين معاً، وأنّ الأقطار تضيق. اختبارٌ
// ينسخ الأرقام يفحص نفسه.
// ============================================================================
const CSS = readFileSync(new URL('./styles.css', import.meta.url), 'utf8');

function blockOf(selector) {
  const start = CSS.indexOf(selector);
  expect(start, `القاعدة ${selector} غير موجودة في styles.css`).toBeGreaterThan(-1);
  return CSS.slice(CSS.indexOf('{', start) + 1, CSS.indexOf('\n}', start));
}

function tokensOf(selector) {
  const out = {};
  for (const [, name, value] of blockOf(selector).matchAll(/(--[a-z-]+)\s*:\s*([^;]+);/g)) out[name] = value.trim();
  return out;
}

const sheet = Object.fromEntries(THEME_PRESETS.map((p) => [p, tokensOf(`[data-preset='${p}']`)]));
const root = tokensOf(':root');
const px = (value) => Number.parseInt(value, 10);

// هويّةٌ ما، كي يكون للظلّ الفاتح لونٌ يُصبغ به — القيمةُ نفسها لا تهمّ، المهمّ شكلُ الناتج.
const brand = { colors: { primary: '#2B6CB0', accent: '#D97706' } };

describe('القوالبُ الثلاثة موجودةٌ ومتمايزة', () => {
  it.each(THEME_PRESETS)('للقالب %s قاعدةٌ في styles.css', (preset) => {
    expect(Object.keys(sheet[preset]).length).toBeGreaterThan(0);
  });

  // العطبُ الذي تُصلحه هذه المرحلة: ثلاثةُ خياراتٍ تُعطي الشكل نفسه.
  it('لا قالبان متطابقان', () => {
    const shapes = THEME_PRESETS.map((p) => JSON.stringify({ ...root, ...sheet[p] }));
    expect(new Set(shapes).size).toBe(THEME_PRESETS.length);
  });

  it('الكلاسيكيُّ هو الافتراض: لا يُزيح رمزاً عن قيمته في :root', () => {
    for (const [name, value] of Object.entries(sheet.classic)) expect(value).toBe(root[name]);
  });

  it('الأقطارُ تضيق من الكلاسيكيّ إلى البسيط إلى الجريء', () => {
    const radius = (p) => px(sheet[p]['--radius'] ?? root['--radius']);
    expect(radius('classic')).toBeGreaterThan(radius('minimal'));
    expect(radius('minimal')).toBeGreaterThan(radius('bold'));
  });

  // شكلٌ وظيفيّ لا ذوقٌ في التخطيط: لصيقةٌ نصفُ قطرها 2px ليست لصيقة.
  it('لا قالب يمسّ --radius-pill', () => {
    for (const preset of THEME_PRESETS) expect(sheet[preset]['--radius-pill']).toBeUndefined();
  });

  // القالبُ يصف كم تفرض الواجهةُ نفسَها، واللونُ والخطُّ ملكُ التاجر.
  it('لا قالب يمسّ لوناً ولا خطّاً', () => {
    for (const preset of THEME_PRESETS) {
      const owned = Object.keys(sheet[preset]).filter((n) => n.startsWith('--color-') || n.startsWith('--font-'));
      expect(owned, `${preset} يُزيح ${owned.join('، ')}`).toEqual([]);
    }
  });
});

describe('الورقةُ والاشتقاقُ يقولان الشيء نفسه', () => {
  const ring = /^0 0 0 \d+px var\(--color-border\)/;

  it.each(['minimal', 'bold'])('%s يستبدل بالظلّ حلقةً في الموضعين', (preset) => {
    expect(sheet[preset]['--shadow'], 'في styles.css').toMatch(ring);

    for (const mode of ['light', 'dark']) {
      const derived = themeVariables(brand, mode, preset);
      expect(derived['--shadow'], `المشتقُّ في الوضع ${mode}`).toMatch(ring);
      expect(derived['--shadow-sm']).toMatch(ring);
    }
  });

  // الحلقةُ تُرسم بـ `--color-border` لا بقيمةٍ ثابتة، فتتبع الوضعَ الداكن بلا سطرٍ ثانٍ.
  it('حلقةُ القالبين تقرأ رمز الحدّ، فلا تحتاج قيمةً لكلّ وضع', () => {
    for (const preset of ['minimal', 'bold']) {
      expect(themeVariables(brand, 'light', preset)['--shadow'])
        .toBe(themeVariables(brand, 'dark', preset)['--shadow']);
    }
  });

  it('الكلاسيكيُّ وحده يطفو، وظلُّه مصبوغٌ بالهوية في الوضع الفاتح', () => {
    const light = themeVariables(brand, 'light', 'classic');
    expect(light['--shadow']).not.toMatch(ring);
    expect(light['--shadow']).toContain('rgba(43, 108, 176');
  });

  // الدرجتان الوسطيّتان لم تكونا تُشتقّان أصلاً، فكانتا تبقيان على قيم الفاتح في الداكن:
  // ظلٌّ أسود على سطحٍ أسود، أي لا ارتفاع.
  it('سُلَّمُ الارتفاع الأربعُ كلُّها مشتقّةٌ لكلّ وضع وقالب', () => {
    for (const preset of THEME_PRESETS) {
      for (const mode of ['light', 'dark']) {
        const derived = themeVariables(brand, mode, preset);
        for (const token of ['--shadow-sm', '--shadow', '--shadow-md', '--shadow-lg']) {
          expect(derived[token], `${token} في ${preset}/${mode}`).toBeTruthy();
        }
      }
    }
  });

  it('القالبُ الافتراضيّ للاشتقاق هو الكلاسيكيّ', () => {
    expect(themeVariables(brand, 'light')).toEqual(themeVariables(brand, 'light', 'classic'));
  });
});
