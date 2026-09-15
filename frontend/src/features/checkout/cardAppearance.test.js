import { describe, it, expect } from 'vitest';
import { cardAppearance, cardFonts } from './cardAppearance';

const from = (values) => (name) => values[name];

describe('cardAppearance', () => {
  it('يأخذ لون المتجر وخطّه من متغيّرات التصميم', () => {
    const options = cardAppearance(from({
      '--font-body': " 'Cairo', sans-serif ", '--color-text': ' #123456 ',
      '--color-text-muted': '#777777', '--color-danger': '#FF0000',
    }));

    expect(options.style.base.fontFamily).toBe("'Cairo', sans-serif");
    expect(options.style.base.color).toBe('#123456');
    expect(options.style.base['::placeholder'].color).toBe('#777777');
    expect(options.style.invalid.color).toBe('#FF0000');
  });

  it('متغيّر غائب أو فارغ ⇒ قيمة احتياطية، لا نصّ فارغ يرفضه Stripe', () => {
    const options = cardAppearance(from({ '--color-text': '   ' }));

    expect(options.style.base.color).toBe('#111827');
    expect(options.style.base.fontFamily).toBe('sans-serif');
  });

  it('لا يمرّر var() إلى Stripe', () => {
    // إطار Stripe لا يرى متغيّرات صفحتنا؛ قيمة غير محسوبة تعني حقلاً بلا لون.
    const options = cardAppearance(from({ '--color-text': 'var(--tenant-text)' }));
    expect(options.style.base.color).not.toContain('var(');
  });
});

describe('cardFonts', () => {
  it('يمرّر ورقة خطّ المتجر لتُحمَّل داخل الإطار', () => {
    expect(cardFonts('https://fonts.example/css2?family=Cairo')).toEqual([
      { cssSrc: 'https://fonts.example/css2?family=Cairo' },
    ]);
  });

  it('بلا ورقة ⇒ لا خيار fonts أصلاً', () => {
    expect(cardFonts(null)).toBeUndefined();
  });
});
