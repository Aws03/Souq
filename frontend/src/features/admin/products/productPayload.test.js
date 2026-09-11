import { describe, expect, it } from 'vitest';
import { buildProductPayload } from './productPayload';

const form = (overrides = {}) => ({
  nameAr: ' سماعات ', nameEn: '', description: ' وصف ', price: '12.345',
  stockQuantity: '5', categoryId: '2', videoRemoved: false, ...overrides,
});

const original = { imageUrl: '/uploads/images/a.png', videoUrl: '/uploads/videos/v.mp4', stockQuantity: 5 };

describe('buildProductPayload', () => {
  it('sends the absolute initial stock when creating a product', () => {
    const payload = buildProductPayload(form({ stockQuantity: '7' }), null);

    expect(payload.stockQuantity).toBe(7);
    expect(payload).not.toHaveProperty('expectedStockQuantity');
    expect(payload.imageUrl).toBe('');
  });

  it('does not touch stock when editing other fields only', () => {
    const payload = buildProductPayload(form({ nameAr: 'اسم جديد' }), original);

    expect(payload).not.toHaveProperty('stockQuantity');
    expect(payload).not.toHaveProperty('expectedStockQuantity');
  });

  it('sends the new stock with the value the admin saw (compare-and-set)', () => {
    const payload = buildProductPayload(form({ stockQuantity: '12' }), original);

    expect(payload.stockQuantity).toBe(12);
    expect(payload.expectedStockQuantity).toBe(5);
  });

  it('keeps 3-decimal JOD prices exact and trims text', () => {
    const payload = buildProductPayload(form(), original);

    expect(payload.price).toBe(12.345);
    expect(payload.nameAr).toBe('سماعات');
    expect(payload.nameEn).toBeNull();
  });

  it('clears the video only when the admin removed it', () => {
    expect(buildProductPayload(form(), original).videoUrl).toBe('/uploads/videos/v.mp4');
    expect(buildProductPayload(form({ videoRemoved: true }), original).videoUrl).toBeNull();
  });
});
