import { describe, expect, it } from 'vitest';
import { buildProductPayload } from './productPayload';

const form = (overrides = {}) => ({
  texts: { ar: { name: ' سماعات ', description: ' وصف ' }, en: { name: '', description: '' } },
  slug: '', sku: ' hp-01 ', brand: '', price: '12.345', compareAtPrice: '', lowStockThreshold: '',
  stockQuantity: '5', categoryId: '2', status: 'Draft', videoRemoved: false, ...overrides,
});

const original = { slug: 'wireless-headphones', videoUrl: '/uploads/videos/v.mp4', onHand: 5, reserved: 1, available: 4 };

describe('buildProductPayload', () => {
  it('opens the stock with the initial quantity, the chosen status and a server-suggested slug on create', () => {
    const payload = buildProductPayload(form({ stockQuantity: '7', lowStockThreshold: '2' }), null);

    expect(payload.stockQuantity).toBe(7);
    expect(payload.lowStockThreshold).toBe(2);
    expect(payload.status).toBe('Draft');
    expect(payload.slug).toBeNull();
  });

  it('omits an empty low-stock threshold on create so the server default applies', () => {
    expect(buildProductPayload(form(), null)).not.toHaveProperty('lowStockThreshold');
  });

  it('never sends stock from the product form on edit (corrections are inventory deltas — C4)', () => {
    const payload = buildProductPayload(form({ stockQuantity: '12', lowStockThreshold: '3' }), original);

    expect(payload).not.toHaveProperty('stockQuantity');
    expect(payload).not.toHaveProperty('expectedStockQuantity');
    expect(payload).not.toHaveProperty('lowStockThreshold');
    expect(payload).not.toHaveProperty('status');
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

  it('trims optional identifiers', () => {
    const payload = buildProductPayload(form(), original);

    expect(payload.sku).toBe('hp-01');
    expect(payload.brand).toBeNull();
  });

  it('clears the video only when the admin removed it', () => {
    expect(buildProductPayload(form(), original).videoUrl).toBe('/uploads/videos/v.mp4');
    expect(buildProductPayload(form({ videoRemoved: true }), original).videoUrl).toBeNull();
  });
});
