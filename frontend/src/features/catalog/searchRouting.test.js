import { describe, it, expect } from 'vitest';
import { isCatalogRoute, isInPlace, searchDestination } from './searchRouting';

// ============================================================================
// البحث حالة رابط. الاختبارات تحرس السلوكين اللذين كانا مفقودين: نتيجة قابلة للمشاركة،
// وتاريخ متصفّح لا يمتلئ بحرف لكل ضغطة.
// ============================================================================
describe('searchDestination', () => {
  it('يضع النصّ في الرابط على صفحة كتالوج', () => {
    expect(searchDestination('/', '', 'shirt')).toEqual({ pathname: '/', search: '?q=shirt' });
  });

  it('يحتفظ بالفلاتر القائمة ويصفّر الصفحة', () => {
    // البحث داخل فئة مختارة يجب أن يبقى داخلها، لكن على صفحتها الأولى.
    const result = searchDestination('/', '?cats=3&page=4', 'shirt');
    const params = new URLSearchParams(result.search);
    expect(params.get('cats')).toBe('3');
    expect(params.get('q')).toBe('shirt');
    expect(params.get('page')).toBeNull();
  });

  it('البحث من صفحة لا تعرض نتائج ينتقل إلى المتجر', () => {
    // من صفحة منتج أو سلّة: البحث فعل تنقّل، لا تعديل حالة في مكانه.
    expect(searchDestination('/products/shirt', '?anything=1', 'boots'))
      .toEqual({ pathname: '/', search: '?q=boots' });
  });

  it('مسح النصّ يزيل المعامل بدل تركه فارغاً', () => {
    expect(searchDestination('/', '?q=shirt', '')).toEqual({ pathname: '/', search: '' });
    expect(searchDestination('/', '?q=shirt', '   ')).toEqual({ pathname: '/', search: '' });
  });

  it('يقصّ الفراغ حول النصّ', () => {
    expect(searchDestination('/', '', '  shirt  ').search).toBe('?q=shirt');
  });

  it('لا ينقل الإصرار على كلمة سابقة إلى كلمة جديدة', () => {
    // ?exact=1 رفضٌ لتصحيح *ذاك* الاستعلام (M3، ADR-0042). لو بقي لكان كل بحث تالٍ بلا استرجاع
    // بلا أن يطلب المتسوّق ذلك — ولا يرى سبباً لغياب "هل تعني…؟".
    const result = searchDestination('/', '?q=مكلسة&exact=1', 'غسالة');
    const params = new URLSearchParams(result.search);
    expect(params.get('q')).toBe('غسالة');
    expect(params.get('exact')).toBeNull();
  });
});

describe('isCatalogRoute and isInPlace', () => {
  it('يعرف صفحات الكتالوج', () => {
    expect(isCatalogRoute('/')).toBe(true);
    expect(isCatalogRoute('/offers')).toBe(true);
    expect(isCatalogRoute('/products/shirt')).toBe(false);
  });

  it('الكتابة في مكانها تستبدل، والانتقال يضيف للتاريخ', () => {
    // بلا هذا يصير زرّ الرجوع يمرّ على كل حرف كُتب في حقل البحث.
    expect(isInPlace('/', searchDestination('/', '', 'a'))).toBe(true);
    expect(isInPlace('/products/shirt', searchDestination('/products/shirt', '', 'a'))).toBe(false);
  });
});
