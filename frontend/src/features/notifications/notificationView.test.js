import { describe, expect, it } from 'vitest';
import { badgeLabel, describeNotification } from './notificationView';

const t = (key, params) => (params ? `${key}|${JSON.stringify(params)}` : key);

describe('notification view', () => {
  it('describes an order status change with the translated status and links to the order', () => {
    const view = describeNotification(
      { kind: 'order.status', data: { orderId: '12', orderNumber: '1001', status: 'Shipped' } }, t);

    expect(view.link).toBe('/orders/12');
    expect(view.text).toContain('notifications.orderStatus');
    expect(view.text).toContain('"number":"1001"');
    expect(view.text).toContain('orders.status.Shipped');
  });

  it('sends staff notifications to the admin pages', () => {
    expect(describeNotification({ kind: 'order.new', data: { orderNumber: '1002' } }, t).link).toBe('/admin/orders');
    const low = describeNotification({ kind: 'stock.low', data: { productName: 'سماعات', available: '3' } }, t);
    expect(low.link).toBe('/admin/inventory');
    expect(low.text).toContain('سماعات');
  });

  it('names the variant of a product with options, and keeps older notifications without one unchanged', () => {
    const seen = [];
    const spy = (key, values) => { seen.push([key, values]); return key; };

    describeNotification({ kind: 'stock.low', data: { productName: 'قميص', variantLabel: 'M / أحمر', available: '2' } }, spy);
    describeNotification({ kind: 'stock.low', data: { productName: 'سماعات', available: '3' } }, spy);

    expect(seen).toEqual([
      ['notifications.lowStockVariant', { name: 'قميص', variant: 'M / أحمر', available: '2' }],
      ['notifications.lowStock', { name: 'سماعات', available: '3' }],
    ]);
  });

  it('falls back to a generic text for an unknown kind', () => {
    expect(describeNotification({ kind: 'future.kind', data: {} }, t)).toEqual({ text: 'notifications.generic', link: null });
    expect(describeNotification(undefined, t).link).toBeNull();
  });

  it('caps the badge at 99+', () => {
    expect(badgeLabel(0)).toBe('');
    expect(badgeLabel(7)).toBe('7');
    expect(badgeLabel(120)).toBe('99+');
  });
});
