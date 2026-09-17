import { describe, it, expect } from 'vitest';
import { buildSynonymPayload, synonymFormProblem, synonymToForm } from './synonymForm';

// ما تحرسه هذه الاختبارات: الواجهة تسبق أخطاء الخادم المعروفة ولا تدّعي معرفة ما لا تعرفه — التساوي بعد
// التطبيع قرار الخادم، فلا يُرفض هنا.
describe('synonymToForm', () => {
  it('يبدأ بالعربية لمفردة جديدة', () => {
    expect(synonymToForm(null)).toEqual({ culture: 'ar', term: '', expansion: '' });
  });

  it('يملأ من صفّ قائم', () => {
    expect(synonymToForm({ culture: 'en', term: 'cell', expansion: 'phone' }))
      .toEqual({ culture: 'en', term: 'cell', expansion: 'phone' });
  });
});

describe('synonymFormProblem', () => {
  it('يقبل زوجاً من كلمتين مفردتين', () => {
    expect(synonymFormProblem({ culture: 'ar', term: 'جوال', expansion: 'هاتف' })).toBeNull();
  });

  it('يرفض الفارغ على أي طرف', () => {
    expect(synonymFormProblem({ culture: 'ar', term: '', expansion: 'هاتف' })).toBe('bothRequired');
    expect(synonymFormProblem({ culture: 'ar', term: 'جوال', expansion: '   ' })).toBe('bothRequired');
  });

  it('يرفض أكثر من كلمة على أي طرف', () => {
    expect(synonymFormProblem({ culture: 'ar', term: 'جوال ذكي', expansion: 'هاتف' })).toBe('singleWord');
    expect(synonymFormProblem({ culture: 'ar', term: 'جوال', expansion: 'هاتف ذكي' })).toBe('singleWord');
  });

  it('لا يرفض ما يقرّره التطبيع على الخادم', () => {
    // "مكنسة" و"مكنسه" كلمة واحدة بعد التطبيع، والخادم يرفضها — والواجهة لا تُكرّر التطبيع كي لا تفترق عنه.
    expect(synonymFormProblem({ culture: 'ar', term: 'مكنسة', expansion: 'مكنسه' })).toBeNull();
  });
});

describe('buildSynonymPayload', () => {
  it('يقصّ الفراغ ويمرّر اللغة', () => {
    expect(buildSynonymPayload({ culture: 'ar', term: '  جوال ', expansion: ' هاتف  ' }))
      .toEqual({ culture: 'ar', term: 'جوال', expansion: 'هاتف' });
  });
});
