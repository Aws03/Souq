import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { contrastRatio } from './app/tenantModel';

// ============================================================================
// رموز الوضع الداكن **قبل** وصول إعداد المتجر.
//
// `tenantModel.themes.test.js` يحرس ما يشتقّه محرّك المظهر، وهو محروسٌ جيّداً. لكنّ بين أول
// رسمٍ ووصول `/api/storefront/config` نافذةً لا يحكمها المحرّك بل قاعدة ثابتة في styles.css —
// ولم يكن يفحصها شيء. فبقيت رموز الهوية والحالة فيها على قيم الوضع الفاتح: زرّ الترتيب
// المختار رُسم `#1f2937` على `#1a1f27` = 1.12:1، و`--color-primary-strong` 1.00:1.
//
// أمسكه axe في رحلة المتجر، لكنّه أمسكه **متنقّلاً**: مرّة على صفحة منتج، ومرّة على الرئيسية،
// ومرّة لا يُمسك — لأنّ ما يُقاس نافذةٌ زمنية لا صفحة. عيبٌ يبدو متقطّعاً يُلاحَق أياماً.
// هذا الملف يقيسه حيث هو ثابت: في الورقة نفسها.
//
// ويُقرأ الملف لا تُنسخ أرقامه: اختبارٌ يعيد كتابة القيم التي يفحصها يفحص نفسه.
// ============================================================================
const CSS = readFileSync(new URL('./styles.css', import.meta.url), 'utf8');

function tokensOf(selector) {
  const start = CSS.indexOf(selector);
  expect(start, `القاعدة ${selector} غير موجودة في styles.css`).toBeGreaterThan(-1);
  const block = CSS.slice(CSS.indexOf('{', start) + 1, CSS.indexOf('\n}', start));
  const out = {};
  for (const [, name, value] of block.matchAll(/(--color-[a-z-]+)\s*:\s*(#[0-9A-Fa-f]{3,8})\s*;/g)) out[name] = value;
  return out;
}

const root = tokensOf(':root');
const dark = { ...root, ...tokensOf("html[data-theme='dark']") };  // الداكن يَرِث ما لا يُعيد تعريفه
const AA = 4.5;

describe('رموز الوضع الداكن قبل وصول إعداد المتجر', () => {
  const surfaces = ['--color-bg', '--color-surface', '--color-surface-alt'];

  // هذه تُستعمل `color:` على أسطح عادية في وحدات CSS — فيسري عليها حدّ النصّ.
  const foregrounds = [
    '--color-text', '--color-text-muted', '--color-primary', '--color-primary-strong',
    '--color-success', '--color-info', '--color-warning', '--color-danger',
  ];

  it.each(foregrounds)('%s يبلغ AA على كل سطح داكن', (token) => {
    expect(dark[token], `${token} غير معرَّف`).toBeTruthy();
    for (const surface of surfaces) {
      const ratio = contrastRatio(dark[token], dark[surface]);
      expect(ratio, `${token} (${dark[token]}) على ${surface} (${dark[surface]}) = ${ratio.toFixed(2)}:1`)
        .toBeGreaterThanOrEqual(AA);
    }
  });

  it('النصّ فوق لون الهوية مقروء — والهوية صارت فاتحة فلا يصلح نصّ فاتح فوقها', () => {
    expect(contrastRatio(dark['--color-on-primary'], dark['--color-primary'])).toBeGreaterThanOrEqual(AA);
  });

  // الحارس الحقيقي: لا يكفي أن تكون القيم صحيحة اليوم، بل ألّا يُضاف رمزٌ أمامي غداً
  // فيُنسى في القاعدة الداكنة كما نُسيت هذه. الوراثة من :root هي بالضبط ما يُخفي السهو.
  it.each(foregrounds)('%s مُعاد تعريفه في القاعدة الداكنة لا موروثاً من الوضع الفاتح', (token) => {
    const darkOnly = tokensOf("html[data-theme='dark']");
    expect(darkOnly[token], `${token} موروث من :root — وقيم :root قيم وضعٍ فاتح`).toBeTruthy();
  });
});
