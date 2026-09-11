import { describe, expect, it } from 'vitest';
import { MAX_WISHLIST, localIds, parseStored, serverUnavailable, toggleLocal } from './wishlistModel';

describe('wishlist model', () => {
  it('sends distinct positive ids in saved order, ignoring malformed items', () => {
    expect(localIds([{ id: 3 }, { id: '7' }, { id: 3 }, { id: 0 }, { id: 'x' }, null, { nameAr: 'قديم' }, { id: 2.5 }]))
      .toEqual([3, 7]);
    expect(localIds(undefined)).toEqual([]);
  });

  it('keeps only the newest items the server can hold', () => {
    const many = Array.from({ length: MAX_WISHLIST + 5 }, (_, i) => ({ id: i + 1 }));
    const ids = localIds(many);
    expect(ids).toHaveLength(MAX_WISHLIST);
    expect(ids[0]).toBe(6);
    expect(ids.at(-1)).toBe(MAX_WISHLIST + 5);
  });

  it('toggles a product in the local list', () => {
    const product = { id: 4, name: 'سماعات' };
    const added = toggleLocal([{ id: 1 }], product);
    expect(added).toEqual([{ id: 1 }, product]);
    expect(toggleLocal(added, product)).toEqual([{ id: 1 }]);
  });

  it('falls back to the local list only when the store or account has no server wishlist', () => {
    expect(serverUnavailable({ code: 'ModuleDisabled', status: 404 })).toBe(true);
    expect(serverUnavailable({ code: 'CustomerAccountRequired', status: 403 })).toBe(true);
    expect(serverUnavailable({ code: 'NotFound', status: 404 })).toBe(false);
    expect(serverUnavailable(new Error('network down'))).toBe(false);
  });

  it('reads a missing or corrupt stored list as empty', () => {
    expect(parseStored(null)).toEqual([]);
    expect(parseStored('{not json')).toEqual([]);
    expect(parseStored('{"id":1}')).toEqual([]);
    expect(parseStored('[{"id":1}]')).toEqual([{ id: 1 }]);
  });
});
