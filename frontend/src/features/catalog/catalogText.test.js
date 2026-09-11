import { describe, expect, it } from 'vitest';
import { formToTexts, hasAnyName, localizedDescription, localizedName, textsToForm } from './catalogText';

const product = {
  name: 'سماعات', description: 'وصف',
  translations: { ar: { name: 'سماعات', description: 'وصف' }, en: { name: 'Headphones', description: null } },
};

describe('localizedName', () => {
  it('shows the interface language when the product has it', () => {
    expect(localizedName(product, 'en')).toBe('Headphones');
    expect(localizedName(product, 'ar')).toBe('سماعات');
  });

  it('falls back to the store default text when the language is missing', () => {
    expect(localizedName({ name: 'سماعات', translations: { ar: { name: 'سماعات' } } }, 'en')).toBe('سماعات');
    expect(localizedDescription(product, 'en')).toBe('وصف');
  });

  it('still reads cart items saved before translations existed', () => {
    expect(localizedName({ nameAr: 'قديم', nameEn: 'Old' }, 'en')).toBe('Old');
    expect(localizedName({ nameAr: 'قديم' }, 'en')).toBe('قديم');
    expect(localizedName(null, 'ar')).toBe('');
  });
});

describe('catalog text form mapping', () => {
  it('round-trips translations and keeps SEO fields the form does not show', () => {
    const form = textsToForm({ ar: { name: 'سماعات', description: 'وصف', metaTitle: 'عنوان', metaDescription: null } });

    expect(form.en).toEqual({ name: '', description: '', metaTitle: null, metaDescription: null });
    expect(formToTexts(form)).toEqual({ ar: { name: 'سماعات', description: 'وصف', metaTitle: 'عنوان', metaDescription: null } });
  });

  it('drops a language without a name and trims the rest', () => {
    const texts = formToTexts({ ar: { name: ' سماعات ', description: '  ' }, en: { name: '   ', description: 'orphan' } });

    expect(texts).toEqual({ ar: { name: 'سماعات', description: null, metaTitle: null, metaDescription: null } });
    expect(hasAnyName({ ar: { name: ' ' }, en: { name: '' } })).toBe(false);
  });
});
