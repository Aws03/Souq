import { describe, it, expect } from 'vitest';
import { contrastRatio, themeVariables } from './tenantModel';

// ============================================================================
// نظام الوضعين. الاختبار هنا ليس "هل اللون جميل؟" بل "هل يبقى مقروءاً مهما كانت هوية المتجر؟"
//
// الوضع مُدخَل لاشتقاق الرموز لا تجاوزاً في CSS، لأن applyStoreTheme يكتب الرموز سطرياً على
// <html> والسطري يعلو أي قاعدة — فقاعدة داكنة في ورقة الأنماط كانت ستُغلَب وتبقى الخلفية بيضاء.
// ============================================================================
const brand = (overrides = {}) => ({
  colors: { primary: '#1F2937', accent: '#D97706', background: '#F9FAFB', text: '#111827', ...overrides },
  typography: 'tajawal',
});

const AA = 4.5;

describe('الوضع الداكن', () => {
  it('يشتقّ خلفية داكنة ونصّاً فاتحاً بدل قراءة خلفية المتجر الفاتحة', () => {
    const light = themeVariables(brand(), 'light');
    const dark = themeVariables(brand(), 'dark');

    expect(light['--color-bg']).toBe('#F9FAFB');
    expect(dark['--color-bg']).not.toBe(light['--color-bg']);
    expect(contrastRatio(dark['--color-text'], dark['--color-bg'])).toBeGreaterThan(AA);
  });

  it('السطح أفتح من الخلفية — الارتفاع بالضوء لا بالظلّ', () => {
    const dark = themeVariables(brand(), 'dark');
    // السطح يجب أن يرتفع عن الخلفية كي تُميَّز البطاقة عنها بلا ظلّ أسود لا يُرى.
    expect(dark['--color-surface']).not.toBe(dark['--color-bg']);
    expect(contrastRatio(dark['--color-surface'], '#000000'))
      .toBeGreaterThan(contrastRatio(dark['--color-bg'], '#000000'));
  });

  it('النصّ الثانوي يبلغ 4.5:1 في الوضعين', () => {
    for (const mode of ['light', 'dark']) {
      const v = themeVariables(brand(), mode);
      expect(contrastRatio(v['--color-text-muted'], v['--color-bg'])).toBeGreaterThanOrEqual(AA);
    }
  });
});

describe('هوية المتجر داخل الوضع الداكن', () => {
  // متجر لونه أسود تقريباً: على خلفية داكنة يختفي تماماً لو تُرك كما هو.
  it('لون هوية داكن جداً يُفتَح حتى يُقرأ', () => {
    const dark = themeVariables(brand({ primary: '#0B0B0B' }), 'dark');

    expect(contrastRatio(dark['--color-primary'], dark['--color-bg'])).toBeGreaterThanOrEqual(AA);
  });

  it('لون هوية مقروء أصلاً يبقى كما هو', () => {
    const vivid = '#FF6B35';
    const dark = themeVariables(brand({ accent: vivid }), 'dark');

    expect(dark['--color-accent']).toBe(vivid);
  });

  it('النصّ فوق لون الهوية مقروء في الوضعين', () => {
    for (const mode of ['light', 'dark']) {
      const v = themeVariables(brand({ primary: '#0B0B0B', accent: '#FFE600' }), mode);
      expect(contrastRatio(v['--color-on-primary'], v['--color-primary'])).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(v['--color-on-accent'], v['--color-accent'])).toBeGreaterThanOrEqual(AA);
    }
  });
});

describe('ألوان الحالة', () => {
  it('تبقى مقروءة في الوضع الداكن بدل أن تختفي', () => {
    const dark = themeVariables(brand(), 'dark');

    for (const token of ['--color-success', '--color-info', '--color-danger']) {
      expect(contrastRatio(dark[token], dark['--color-bg'])).toBeGreaterThanOrEqual(AA);
    }
  });

  it('لا يُعيد متجرٌ تعريف معنى "خطر"', () => {
    // الأحمر ليس خيار علامة: لون الحالة مشتقّ من أساس ثابت لا من إعداد المتجر.
    const a = themeVariables(brand({ primary: '#004E98', accent: '#3A6EA5' }), 'light');
    const b = themeVariables(brand({ primary: '#7B1E3A', accent: '#C09891' }), 'light');

    expect(a['--color-danger']).toBe(b['--color-danger']);
  });
});

describe('الرموز التي تعتمد عليها المكوّنات', () => {
  it('كل رمز لوني معرَّف في الوضعين — لا رمز يسقط فيرث قيمة الوضع الآخر', () => {
    const light = Object.keys(themeVariables(brand(), 'light'));
    const dark = Object.keys(themeVariables(brand(), 'dark'));

    expect(new Set(dark)).toEqual(new Set(light));
  });

  it('الظلّ في الداكن ليس ظلّاً أسود وحده', () => {
    // ظلّ أسود على سطح داكن غير مرئي: الارتفاع هناك حدٌّ مضيء.
    expect(themeVariables(brand(), 'dark')['--shadow']).toContain('255, 255, 255');
  });
});

// ============================================================================
// اللوحة المقلوبة — التذييل، والرأسية، وشريط الإدارة الجانبي.
//
// هذه كانت تُبنى من رمزين عامّين: `--color-primary` خلفيةً و`--color-bg` نصّاً. وهو صحيح في
// الفاتح ومكسور في الداكن، لأن الرمزين ينقلبان معاً — الهوية تُفتَح لتبقى مقروءة على خلفية
// داكنة، والخلفية تسودّ. فظهر شريط الإدارة رمادياً باهتاً باسم متجرٍ لا يكاد يُقرأ عليه.
//
// ولا يُمسك ذلك بفحص تباينٍ على الرموز العامّة: كلٌّ منهما سليم وحده، والعطب في اقترانهما.
// ============================================================================
describe('اللوحة المقلوبة', () => {
  it('تبقى داكنة في الوضعين — لا تنقلب مع انقلاب الخلفية', () => {
    for (const mode of ['light', 'dark']) {
      const v = themeVariables(brand(), mode);
      // أدكن من الأبيض بفارق واضح: لوحة فاتحة بنصّ فاتح هي بالضبط العطب المقصود.
      expect(contrastRatio(v['--color-panel'], '#FFFFFF')).toBeGreaterThan(3);
    }
  });

  it('نصّ اللوحة يبلغ 4.5:1 عليها في الوضعين', () => {
    for (const mode of ['light', 'dark']) {
      const v = themeVariables(brand(), mode);
      expect(contrastRatio(v['--color-on-panel'], v['--color-panel'])).toBeGreaterThanOrEqual(AA);
    }
  });

  it('لون التمييز على اللوحة يُقرأ عليها لا على خلفية الصفحة', () => {
    for (const mode of ['light', 'dark']) {
      const v = themeVariables(brand(), mode);
      expect(contrastRatio(v['--color-accent-on-panel'], v['--color-panel'])).toBeGreaterThanOrEqual(AA);
    }
  });

  it('هوية فاتحة جداً لا تُنتج لوحة بيضاء', () => {
    // متجر لونه الأساسي أبيض تقريباً: اللوحة تبقى صالحة سطحاً للنصّ الفاتح أو تُقرأ بنصٍّ داكن.
    for (const mode of ['light', 'dark']) {
      const v = themeVariables(brand({ primary: '#FAFAFA' }), mode);
      expect(contrastRatio(v['--color-on-panel'], v['--color-panel'])).toBeGreaterThanOrEqual(AA);
    }
  });
});

// ============================================================================
// التعبئة: زرٌّ ممتلئ بلون الهوية. النصّ عليه يُشتقّ من اللون *بعد* تحويله للوضع، لا من إعداد
// المتجر — التاجر ضبط onPrimary مقابل هويته الفاتحة، وفي الداكن صارت أفتح، فالأبيض المحفوظ
// يصير أبيض على رمادي: زرٌّ يبدو معطّلاً وهو الزرّ الرئيسي في المتجر.
// ============================================================================
describe('النصّ على التعبئة', () => {
  it('نصّ الزرّ الأساسي يبلغ 4.5:1 على تعبئته في الوضعين', () => {
    for (const mode of ['light', 'dark']) {
      const v = themeVariables(brand(), mode);
      expect(contrastRatio(v['--color-on-primary'], v['--color-primary'])).toBeGreaterThanOrEqual(AA);
      expect(contrastRatio(v['--color-on-accent'], v['--color-accent'])).toBeGreaterThanOrEqual(AA);
    }
  });

  it('حتى حين يضبط المتجر onPrimary بما لا يصلح للوضع الداكن', () => {
    const v = themeVariables(brand({ onPrimary: '#FFFFFF' }), 'dark');
    expect(contrastRatio(v['--color-on-primary'], v['--color-primary'])).toBeGreaterThanOrEqual(AA);
  });
});
