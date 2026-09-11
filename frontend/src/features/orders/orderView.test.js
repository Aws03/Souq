import { describe, expect, it } from 'vitest';
import { toQueryString } from '../../api/query';
import { actorLabel, buildOrderQuery, trackingUrl } from './orderView';

const t = (key, { defaultValue } = {}) => ({ 'admin.orders.actor.PaymentGateway': 'بوّابة الدفع' }[key] ?? defaultValue);

describe('order view', () => {
  it('builds the public tracking link from the random token, never the order id', () => {
    expect(trackingUrl('https://shop.example/', 'a'.repeat(32))).toBe(`https://shop.example/track/${'a'.repeat(32)}`);
    expect(trackingUrl('https://shop.example', 'abc')).toBe('https://shop.example/track/abc');
  });

  it('labels who changed a status: staff by name, otherwise the actor kind', () => {
    expect(actorLabel({ changedBy: 'Staff', changedByName: 'سارة' }, t)).toBe('سارة');
    expect(actorLabel({ changedBy: 'PaymentGateway' }, t)).toBe('بوّابة الدفع');
    expect(actorLabel({ changedBy: 'Staff' }, t)).toBe('Staff');
    expect(actorLabel({ changedBy: null }, t)).toBeNull();
  });

  it('drops empty filters from the admin order query', () => {
    expect(toQueryString(buildOrderQuery({ status: '', search: '  ', page: 1, pageSize: 20 }))).toBe('?page=1&pageSize=20');
    expect(toQueryString(buildOrderQuery({ status: 'Paid', search: ' #1042 ', page: 2, pageSize: 20 })))
      .toBe('?status=Paid&search=%231042&page=2&pageSize=20');
  });
});
