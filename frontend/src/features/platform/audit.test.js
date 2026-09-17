import { describe, it, expect } from 'vitest';
import ar from '../../i18n/locales/ar.json';
import en from '../../i18n/locales/en.json';
import {
  ACTION_GROUPS, KNOWN_ACTIONS, actionKey, actionsInGroup, actorKind, auditQuery, dayEndUtc, dayStartUtc, emptyFilters,
  filterProblems, filtersFromSearch, groupOf, pageFromSearch, readMetadata, searchFromFilters,
} from './audit';

// ============================================================================
// سجلّ التدقيق — المرشّحات في الرابط وإلى الخادم، واللحظة بتوقيت UTC، وقراءة السطر كما خُزّن.
// ============================================================================
describe('المرشّحات والرابط', () => {
  it('الرابط يحمل ما ضُبط فقط، ويُقرأ كما كُتب', () => {
    const filters = { ...emptyFilters(), tenantId: '12', action: 'tenant.', from: '2026-09-01' };
    const search = searchFromFilters(filters, 3);
    expect(search.toString()).toBe('tenantId=12&action=tenant.&from=2026-09-01&page=3');
    expect(filtersFromSearch(search)).toEqual(filters);
    expect(pageFromSearch(search)).toBe(3);
  });

  it('قيمٌ عبثية في الرابط تُهمل لا تُرسل', () => {
    const search = new URLSearchParams('tenantId=abc&actorUserId=-4&from=17/09/2026&page=0&action=' + 'x'.repeat(81));
    expect(filtersFromSearch(search)).toEqual(emptyFilters());
    expect(pageFromSearch(search)).toBe(1);
  });

  it('مدّة معكوسة ورقم غير صحيح تُقال قبل الإرسال', () => {
    expect(filterProblems({ ...emptyFilters(), tenantId: '1.5', from: '2026-09-10', to: '2026-09-01' }))
      .toEqual({ tenantId: 'idInvalid', to: 'rangeReversed' });
    expect(filterProblems({ ...emptyFilters(), from: '2026-09-01', to: '2026-09-01' })).toEqual({});
  });

  it('اليوم بتوقيت من يحقّق، و"إلى" تشمل يومها كاملاً', () => {
    const start = new Date(dayStartUtc('2026-09-17'));
    const end = new Date(dayEndUtc('2026-09-17'));
    expect([start.getFullYear(), start.getMonth(), start.getDate(), start.getHours(), start.getMinutes()]).toEqual([2026, 8, 17, 0, 0]);
    expect([end.getDate(), end.getHours(), end.getMinutes(), end.getSeconds()]).toEqual([17, 23, 59, 59]);
    expect(end.getTime() - start.getTime()).toBe(24 * 60 * 60 * 1000 - 1);
  });

  it('معاملات الخادم: ترقيم من الخادم بحجم ثابت، ولا مفاتيح فارغة', () => {
    expect(auditQuery(emptyFilters(), 2)).toEqual({
      tenantId: undefined, action: undefined, actorUserId: undefined, from: undefined, to: undefined, page: 2, pageSize: 50,
    });
    const query = auditQuery({ ...emptyFilters(), actorUserId: '7', to: '2026-09-17' }, 1);
    expect(query.actorUserId).toBe('7');
    expect(query.to).toBe(dayEndUtc('2026-09-17'));
  });
});

describe('قراءة السطر', () => {
  it('البيانات الوصفية: كائن ⇒ أزواج، ومقصوص أو غير كائن ⇒ النصّ كما خُزّن', () => {
    expect(readMetadata('{"role":"PlatformAdmin","actorUserId":null,"list":["a"]}').entries)
      .toEqual([['role', 'PlatformAdmin'], ['actorUserId', null], ['list', '["a"]']]);
    expect(readMetadata('{"action":"tenant.cre')).toEqual({ entries: [], raw: '{"action":"tenant.cre' });
    expect(readMetadata(null)).toEqual({ entries: [], raw: null });
  });

  it('لا يُنسب سطر بلا حساب إلى أحد', () => {
    expect(actorKind({ actorUserId: null, area: 'System' }, 1)).toBe('system');
    expect(actorKind({ actorUserId: null, area: 'Store' }, 1)).toBe('unknown');
    expect(actorKind({ actorUserId: 1, area: 'Platform' }, 1)).toBe('you');
    expect(actorKind({ actorUserId: 2, area: 'Platform' }, 1)).toBe('account');
  });
});

describe('فئات الأفعال', () => {
  it('الأطول بادئةً يغلب، وكل فعل معروف في فئة واحدة', () => {
    expect(groupOf('platform.user.disabled')).toBe('platformAccounts');
    expect(groupOf('platform.users.listed')).toBe('platformReads');
    expect(groupOf('unheard.of')).toBeNull();
    const grouped = ACTION_GROUPS.flatMap((g) => actionsInGroup(g.key));
    expect(grouped.sort()).toEqual([...KNOWN_ACTIONS].sort());
  });

  it('لكل فعل معروف ولكل فئة عنوان باللغتين', () => {
    for (const bundle of [en, ar]) {
      for (const action of KNOWN_ACTIONS) expect(bundle.platform.audit.action[actionKey(action)], action).toBeTruthy();
      for (const group of ACTION_GROUPS) expect(bundle.platform.audit.group[group.key], group.key).toBeTruthy();
    }
  });
});
