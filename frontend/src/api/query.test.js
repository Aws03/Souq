import { describe, expect, it } from 'vitest';
import { toQueryString } from './query';

describe('toQueryString', () => {
  it('returns an empty string when there is nothing to send', () => {
    expect(toQueryString()).toBe('');
    expect(toQueryString({ keyword: '', status: null, sort: undefined })).toBe('');
  });

  it('keeps zero and false, which are real values', () => {
    expect(toQueryString({ minPrice: 0, active: false })).toBe('?minPrice=0&active=false');
  });

  it('repeats the key for arrays, the shape ASP.NET binds to List<int>', () => {
    expect(toQueryString({ categoryIds: [1, 2, '', null] })).toBe('?categoryIds=1&categoryIds=2');
  });

  it('encodes text safely', () => {
    expect(toQueryString({ keyword: 'سماعات & more', page: 2 })).toBe(
      `?keyword=${encodeURIComponent('سماعات')}+%26+more&page=2`,
    );
  });
});
