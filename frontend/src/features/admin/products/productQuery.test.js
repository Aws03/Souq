import { describe, expect, it } from 'vitest';
import { buildAdminProductQuery } from './productQuery';

describe('buildAdminProductQuery', () => {
  it('sends the selected category as categoryIds (the key the API binds)', () => {
    const query = buildAdminProductQuery({ keyword: '', categoryId: '3', page: 1, pageSize: 10 });

    expect(query.categoryIds).toEqual([3]);
    expect(query).not.toHaveProperty('categoryId');
  });

  it('omits empty filters so they are not sent at all', () => {
    const query = buildAdminProductQuery({ keyword: '   ', categoryId: '', page: 2, pageSize: 10 });

    expect(query).toEqual({ keyword: undefined, categoryIds: undefined, page: 2, pageSize: 10 });
  });

  it('trims the keyword', () => {
    expect(buildAdminProductQuery({ keyword: ' سماعات ', page: 1, pageSize: 10 }).keyword).toBe('سماعات');
  });
});
