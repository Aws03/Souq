import { describe, it, expect } from 'vitest';
import { canonicalUrl, pageTitle, robotsFor, socialTags } from './pageMetadata';

// ============================================================================
// SEO لكل مستأجر (المرحلة 16): كل صفحة كانت تحمل عنوان المتجر ووصفه نفسيهما، فلا صفحة
// منتج تُصنَّف على اسمها ولا رابط مُشارَك يعرض ما يخصّه.
// ============================================================================
describe('pageTitle', () => {
  it('يضمّ اسم الصفحة إلى اسم المتجر', () => {
    expect(pageTitle('Wireless headphones', 'Marka')).toBe('Wireless headphones — Marka');
  });

  it('لا يكرّر الاسم على الرئيسية', () => {
    expect(pageTitle('Marka', 'Marka')).toBe('Marka');
    expect(pageTitle('', 'Marka')).toBe('Marka');
  });

  it('يتحمّل غياب إعداد المتجر', () => {
    expect(pageTitle('Cart', undefined)).toBe('Cart');
    expect(pageTitle(undefined, undefined)).toBe('');
  });
});

describe('canonicalUrl', () => {
  it('يحتفظ بما يغيّر المحتوى فعلاً', () => {
    expect(canonicalUrl('https://store.example', '/', '?cats=3&page=2'))
      .toBe('https://store.example/?cats=3&page=2');
  });

  it('يسقط معاملات العرض والتتبّع — ترتيب مختلف ليس صفحة أخرى', () => {
    // بلا هذا تُفهرس عشرات الروابط لمحتوى واحد، فيتشتّت ترتيبها كلّه.
    expect(canonicalUrl('https://store.example', '/', '?view=list&sort=priceAsc&utm_source=ig'))
      .toBe('https://store.example/');
  });

  it('لا يكرّر الشرطة المائلة', () => {
    expect(canonicalUrl('https://store.example/', '/products/shirt', ''))
      .toBe('https://store.example/products/shirt');
  });
});

describe('robotsFor', () => {
  it('يمنع فهرسة صفحات الزائر الخاصّة', () => {
    for (const path of ['/account', '/orders', '/orders/12', '/checkout', '/confirmation', '/track/abc'])
      expect(robotsFor(path)).toBe('noindex, nofollow');
  });

  it('يترك صفحات المتجر العامّة تُفهرَس', () => {
    for (const path of ['/', '/products/shirt', '/offers'])
      expect(robotsFor(path)).toBeNull();
  });

  it('لا يخلط مساراً يبدأ بالنصّ نفسه', () => {
    expect(robotsFor('/accountants-guide')).toBeNull();
  });
});

describe('socialTags', () => {
  it('يبني وسوم المشاركة ويسقط الفارغ', () => {
    const tags = Object.fromEntries(socialTags({ title: 'T', description: 'D', url: 'U' }));
    expect(tags['og:title']).toBe('T');
    expect(tags['og:image']).toBeUndefined();
    expect(tags['twitter:card']).toBe('summary');
  });

  it('صورة المنتج تجعل البطاقة كبيرة', () => {
    const tags = Object.fromEntries(socialTags({ title: 'T', image: '/uploads/p.png' }));
    expect(tags['twitter:card']).toBe('summary_large_image');
  });
});
