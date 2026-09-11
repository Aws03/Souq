import { describe, expect, it } from 'vitest';
import { activationPayload, buildCategoryPayload, descendantIds, orderAsTree } from './categoryForm';

const category = (id, parentId = null, sortOrder = 0) => ({ id, parentId, sortOrder, slug: `c-${id}`, name: `c${id}` });

describe('orderAsTree', () => {
  it('lists every parent before its children, siblings by sort order then id, with depth', () => {
    const tree = orderAsTree([category(3, 1, 1), category(1), category(2, 1, 0), category(4, 2), category(5, null, -1)]);

    expect(tree.map((c) => [c.id, c.depth])).toEqual([[5, 0], [1, 0], [2, 1], [4, 2], [3, 1]]);
  });

  it('keeps rows whose parent is missing or that sit in a cycle visible', () => {
    const tree = orderAsTree([category(1, 99), category(2, 3), category(3, 2)]);

    expect(tree.map((c) => c.id).sort()).toEqual([1, 2, 3]);
  });
});

describe('descendantIds', () => {
  it('finds children and grandchildren but not the category itself or siblings', () => {
    const categories = [category(1), category(2, 1), category(3, 2), category(4)];

    expect([...descendantIds(categories, 1)].sort()).toEqual([2, 3]);
    expect(descendantIds(categories, 4).size).toBe(0);
  });
});

describe('category payloads', () => {
  it('sends texts per language, a normalized slug and numeric fields', () => {
    const payload = buildCategoryPayload({
      slug: ' Home-Decor ', texts: { ar: { name: ' ديكور ' }, en: { name: '' } }, parentId: '4', sortOrder: '2', isActive: false,
    });

    expect(payload).toEqual({
      slug: 'home-decor',
      translations: { ar: { name: 'ديكور', description: null, metaTitle: null, metaDescription: null } },
      parentId: 4, sortOrder: 2, isActive: false,
    });
    expect(buildCategoryPayload({ slug: 'x1', texts: {}, parentId: '', sortOrder: '', isActive: true }).parentId).toBeNull();
  });

  it('toggles activation without dropping the other fields', () => {
    const translations = { ar: { name: 'هواتف', description: null, metaTitle: null, metaDescription: null } };

    expect(activationPayload({ ...category(7, 2, 3), translations, isActive: true }, false))
      .toEqual({ slug: 'c-7', translations, parentId: 2, sortOrder: 3, isActive: false });
  });
});
