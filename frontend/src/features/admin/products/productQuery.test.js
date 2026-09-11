import { describe, expect, it } from 'vitest';
import { buildAdminProductQuery } from './productQuery';

describe('buildAdminProductQuery', () => {
  it('sends the selected category as categoryId (the key GET /admin/products binds)', () => {
    const query = buildAdminProductQuery({ keyword: '', categoryId: '3', page: 1, pageSize: 10 });

    expect(query.categoryId).toBe(3);
    expect(query).not.toHaveProperty('categoryIds');
  });

  it('sends the status filter as the enum name', () => {
    expect(buildAdminProductQuery({ status: 'Archived', page: 1, pageSize: 10 }).status).toBe('Archived');
  });

  it('omits empty filters so they are not sent at all', () => {
    const query = buildAdminProductQuery({ keyword: '   ', categoryId: '', status: '', page: 2, pageSize: 10 });

    expect(query).toEqual({ keyword: undefined, categoryId: undefined, status: undefined, page: 2, pageSize: 10 });
  });

  it('trims the keyword', () => {
    expect(buildAdminProductQuery({ keyword: ' سماعات ', page: 1, pageSize: 10 }).keyword).toBe('سماعات');
  });
});
