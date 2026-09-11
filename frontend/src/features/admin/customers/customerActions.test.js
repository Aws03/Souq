import { describe, expect, it } from 'vitest';
import { toQueryString } from '../../../api/query';
import { buildCustomerQuery, customerActions, nextStatus } from './customerActions';

describe('admin customer actions', () => {
  it('offers block or unblock, export and erase to a manager', () => {
    expect(customerActions({ status: 'Active', isErased: false }, true)).toEqual(['Block', 'Export', 'Erase']);
    expect(customerActions({ status: 'Blocked', isErased: false }, true)).toEqual(['Unblock', 'Export', 'Erase']);
  });

  it('offers nothing without customers.manage or for an erased customer', () => {
    expect(customerActions({ status: 'Active', isErased: false }, false)).toEqual([]);
    expect(customerActions({ status: 'Blocked', isErased: true }, true)).toEqual([]);
    expect(customerActions(null, true)).toEqual([]);
  });

  it('maps block and unblock to the status the server expects', () => {
    expect(nextStatus('Block')).toBe('Blocked');
    expect(nextStatus('Unblock')).toBe('Active');
  });

  it('drops empty filters from the list query', () => {
    expect(toQueryString(buildCustomerQuery({ keyword: '  ', status: '', page: 2, pageSize: 20 }))).toBe('?page=2&pageSize=20');
    expect(toQueryString(buildCustomerQuery({ keyword: ' sara ', status: 'Blocked', page: 1, pageSize: 20 })))
      .toBe('?keyword=sara&status=Blocked&page=1&pageSize=20');
  });
});
