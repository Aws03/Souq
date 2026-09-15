import { describe, it, expect } from 'vitest';
import { isProductId, needsCanonicalRedirect, productPath } from './productRouting';

describe('productPath', () => {
  it('يفضّل الـ slug', () => {
    expect(productPath({ id: 42, slug: 'blue-shirt' })).toBe('/products/blue-shirt');
  });

  it('يسقط إلى المعرّف حين لا slug', () => {
    expect(productPath({ id: 42, slug: '' })).toBe('/products/42');
    expect(productPath({ id: 42 })).toBe('/products/42');
  });
});

describe('isProductId', () => {
  it('الأرقام وحدها معرّف', () => {
    expect(isProductId('42')).toBe(true);
    expect(isProductId('blue-shirt')).toBe(false);
    expect(isProductId('2-in-1-kit')).toBe(false);
  });

  it('لا يعدّ الصفر ولا الفراغ ولا السالب معرّفاً', () => {
    // "/products/0" و"/products/" يجب أن يذهبا لمسار الـ slug فيردّ الخادم 404، لا أن يُطلب معرّف 0.
    for (const handle of ['0', '', '  ', '-1', '007', undefined, null]) {
      expect(isProductId(handle)).toBe(false);
    }
  });
});

describe('needsCanonicalRedirect', () => {
  it('الدخول بالمعرّف يُحوَّل إلى الاسم', () => {
    expect(needsCanonicalRedirect('42', { id: 42, slug: 'blue-shirt' })).toBe(true);
  });

  it('الدخول بالاسم القانوني لا يُحوَّل', () => {
    expect(needsCanonicalRedirect('blue-shirt', { id: 42, slug: 'blue-shirt' })).toBe(false);
  });

  it('منتج بلا slug يبقى على معرّفه بلا تحويل لا نهائي', () => {
    expect(needsCanonicalRedirect('42', { id: 42, slug: null })).toBe(false);
  });
});
