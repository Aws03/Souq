import { describe, it, expect } from 'vitest';
import { breadcrumbStructuredData, productStructuredData } from './structuredData';

const product = {
  id: 7, slug: 'blue-shirt', price: 19.9, currency: 'USD', stockQuantity: 4,
  brand: 'Acme', categoryName: 'Shirts',
};

const base = { product, url: 'https://shop.example/products/blue-shirt', name: 'Blue shirt' };

describe('productStructuredData', () => {
  it('يبني عرضاً بسعر المنتج وعملته وتوفّره', () => {
    const data = productStructuredData(base);

    expect(data['@type']).toBe('Product');
    expect(data.offers).toMatchObject({
      price: '19.9', priceCurrency: 'USD', availability: 'https://schema.org/InStock',
    });
  });

  it('نفاد المخزون يُعلَن لا يُخفى', () => {
    const data = productStructuredData({ ...base, product: { ...product, stockQuantity: 0 } });
    expect(data.offers.availability).toBe('https://schema.org/OutOfStock');
  });

  it('لا تقييم مجمّع بلا مقيّمين', () => {
    // المحرّكات تعاقب aggregateRating بعدد صفر، ولا يوجد ما يُجمَّع أصلاً.
    expect(productStructuredData({ ...base, rating: { average: 0, count: 0 } })).not.toHaveProperty('aggregateRating');
    expect(productStructuredData({ ...base, rating: { average: 4.5, count: 3 } }).aggregateRating).toMatchObject({
      ratingValue: '4.5', reviewCount: 3,
    });
  });

  it('يحذف ما لا يعرفه بدل ملئه', () => {
    const data = productStructuredData({ product: { id: 1 }, url: 'u', name: 'n' });

    expect(data).not.toHaveProperty('offers');
    expect(data).not.toHaveProperty('brand');
    expect(data).not.toHaveProperty('image');
    expect(data).not.toHaveProperty('description');
  });

  it('بلا منتج ⇒ لا بيان', () => {
    expect(productStructuredData({ product: null, url: 'u', name: 'n' })).toBeNull();
  });
});

describe('breadcrumbStructuredData', () => {
  it('يرقّم الخطوات من واحد', () => {
    const data = breadcrumbStructuredData([
      { name: 'Store', url: 'https://shop.example/' },
      { name: 'Shirts', url: 'https://shop.example/?cats=3' },
    ]);

    expect(data.itemListElement.map((i) => i.position)).toEqual([1, 2]);
    expect(data.itemListElement[1].item).toBe('https://shop.example/?cats=3');
  });

  it('يتجاهل خطوة بلا اسم أو بلا رابط', () => {
    const data = breadcrumbStructuredData([{ name: 'Store', url: '/' }, { name: 'Shirts' }]);
    expect(data.itemListElement).toHaveLength(1);
  });

  it('لا خطوات ⇒ لا بيان', () => {
    expect(breadcrumbStructuredData([])).toBeNull();
    expect(breadcrumbStructuredData(null)).toBeNull();
  });
});
