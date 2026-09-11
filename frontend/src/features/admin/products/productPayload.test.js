import { describe, expect, it } from 'vitest';
import { buildProductPayload } from './productPayload';

const form = (overrides = {}) => ({
  texts: { ar: { name: ' سماعات ', description: ' وصف ' }, en: { name: '', description: '' } },
  slug: '', sku: ' hp-01 ', brand: '', price: '12.345', compareAtPrice: '', lowStockThreshold: '',
  stockQuantity: '5', categoryId: '2', status: 'Draft', videoRemoved: false, ...overrides,
});

const original = { slug: 'wireless-headphones', videoUrl: '/uploads/videos/v.mp4', stockQuantity: 5 };

describe('buildProductPayload', () => {
  it('sends the absolute initial stock, the chosen status and lets the server suggest the slug on create', () => {
    const payload = buildProductPayload(form({ stockQuantity: '7' }), null);

    expect(payload.stockQuantity).toBe(7);
    expect(payload).not.toHaveProperty('expectedStockQuantity');
    expect(payload.status).toBe('Draft');
    expect(payload.slug).toBeNull();
  });

  it('does not touch stock when editing other fields only', () => {
    const payload = buildProductPayload(form({ texts: { ar: { name: 'اسم جديد' } } }), original);

    expect(payload).not.toHaveProperty('stockQuantity');
    expect(payload).not.toHaveProperty('expectedStockQuantity');
    expect(payload).not.toHaveProperty('status');
  });

  it('sends the new stock with the value the admin saw (compare-and-set)', () => {
    const payload = buildProductPayload(form({ stockQuantity: '12' }), original);

    expect(payload.stockQuantity).toBe(12);
    expect(payload.expectedStockQuantity).toBe(5);
  });

  it('sends texts per language, dropping a language without a name', () => {
    const payload = buildProductPayload(form(), original);

    expect(payload.translations).toEqual({ ar: { name: 'سماعات', description: 'وصف', metaTitle: null, metaDescription: null } });
    expect(payload).not.toHaveProperty('nameAr');
  });

  it('keeps 3-decimal JOD prices exact and sends compare-at only when given', () => {
    expect(buildProductPayload(form(), original).price).toBe(12.345);
    expect(buildProductPayload(form(), original).compareAtPrice).toBeNull();
    expect(buildProductPayload(form({ compareAtPrice: '15.500' }), original).compareAtPrice).toBe(15.5);
  });

  it('keeps the current slug when the field is cleared on edit, and normalizes a typed one', () => {
    expect(buildProductPayload(form(), original).slug).toBe('wireless-headphones');
    expect(buildProductPayload(form({ slug: ' New-Slug ' }), original).slug).toBe('new-slug');
  });

  it('trims optional identifiers and omits an empty low-stock threshold', () => {
    const payload = buildProductPayload(form(), original);

    expect(payload.sku).toBe('hp-01');
    expect(payload.brand).toBeNull();
    expect(payload).not.toHaveProperty('lowStockThreshold');
    expect(buildProductPayload(form({ lowStockThreshold: '0' }), original).lowStockThreshold).toBe(0);
  });

  it('clears the video only when the admin removed it', () => {
    expect(buildProductPayload(form(), original).videoUrl).toBe('/uploads/videos/v.mp4');
    expect(buildProductPayload(form({ videoRemoved: true }), original).videoUrl).toBeNull();
  });
});
