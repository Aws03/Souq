import { describe, expect, it } from 'vitest';
import {
  displayStatus, filtersFromSearch, hasOutstanding, invoiceQuery, isDraft, isIssued, issueBlocker,
  lineProblems, pageFromSearch, paymentProblems, previewLineTotal, searchFromFilters,
} from './invoices';

// ============================================================================
// منطقُ شاشات الفوترة (C5، ADR-0056) — نقيٌّ ويُختبر بلا DOM.
//
// **وأهمُّ ما هنا هو ما يُخفى ومتى**: زرُّ الإصدار على مسوّدةٍ بلا أسطر، وزرُّ السداد على مستندٍ
// سُدّد، وبابُ التحرير على فاتورةٍ صدرت. الخادمُ يرفض كلَّ واحدةٍ منها بـ 422 — فما يفعله هذا
// المنطق ليس الحماية بل منعُ ضغطةٍ جوابُها معروفٌ سلفاً بالرفض.
// ============================================================================
describe('حالةُ العرض', () => {
  it('الصادرةُ المتأخّرة تُقرأ «متأخّرة» لا «صادرة»', () => {
    // «متأخّرة» ليست حالةً يرسلها الخادم — هي `Issued` ومرّ استحقاقُها. ودمجُها هنا يجعل الشارة
    // تقول ما يهمّ فعلاً بدل «صادرة» لفاتورةٍ تأخّرت شهراً.
    expect(displayStatus({ status: 'Issued', isOverdue: true })).toBe('Overdue');
    expect(displayStatus({ status: 'Issued', isOverdue: false })).toBe('Issued');
  });

  it('لا تُعَدّ مسدَّدةٌ متأخّرةً مهما قال العلَم', () => {
    // الخادم لا يرسل `isOverdue` على مسدَّدة أصلاً؛ وهذا يحرس الطرفَ الآخر: لو أرسلها يوماً
    // فلن تُقرأ الفاتورةُ المسدَّدة «متأخّرة» في شاشة تاجر.
    expect(displayStatus({ status: 'Settled', isOverdue: true })).toBe('Settled');
  });

  it('بلا فاتورةٍ أصلاً ⇒ مسوّدة، لا انهيار', () => {
    expect(displayStatus(null)).toBe('Draft');
    expect(displayStatus(undefined)).toBe('Draft');
  });
});

describe('ما يُتاح على المستند', () => {
  it('المسوّدةُ تُحرَّر والصادرةُ لا', () => {
    expect(isDraft({ status: 'Draft' })).toBe(true);
    expect(isDraft({ status: 'Issued' })).toBe(false);
    expect(isIssued({ status: 'Issued' })).toBe(true);
    expect(isIssued({ status: 'Settled' })).toBe(true);
    expect(isIssued({ status: 'Draft' })).toBe(false);
    expect(isIssued({ status: 'Cancelled' })).toBe(false);
  });

  it('السدادُ وإشعارُ الدائن على ما بقي عليه شيءٌ وحده', () => {
    expect(hasOutstanding({ status: 'Issued', outstanding: 10 })).toBe(true);
    expect(hasOutstanding({ status: 'Issued', outstanding: 0 })).toBe(false);
    expect(hasOutstanding({ status: 'Settled', outstanding: 0 })).toBe(false);
    // ومسوّدةٌ لا تُسدَّد ولو حُسب لها إجمالي: لم تصدر بعد.
    expect(hasOutstanding({ status: 'Draft', outstanding: 99 })).toBe(false);
  });
});

describe('ما يمنع الإصدار', () => {
  const ready = { canIssue: true, blockingReason: null };
  const draft = { status: 'Draft', lines: [{ id: 1 }] };

  it('إعدادُ المنصّة أوّلاً: سببُه يسبق سببَ الفاتورة', () => {
    // «اضبط العملة» تسبق «أضف سطراً»: الأولى تمنع كلَّ فاتورة، والثانية تمنع هذه وحدها.
    expect(issueBlocker({ canIssue: false, blockingReason: 'BillingCurrencyNotSet' }, { status: 'Draft', lines: [] }))
      .toBe('BillingCurrencyNotSet');
  });

  it('بلا إعدادٍ أصلاً ⇒ سببٌ مسمّى لا فراغ', () => {
    expect(issueBlocker(null, draft)).toBe('BillingSettingsMissing');
    expect(issueBlocker(undefined, draft)).toBe('BillingSettingsMissing');
  });

  it('مسوّدةٌ بلا أسطر لا تُصدَر', () => {
    expect(issueBlocker(ready, { status: 'Draft', lines: [] })).toBe('NoLines');
  });

  it('الصادرةُ لا تُصدَر مرّةً ثانية', () => {
    expect(issueBlocker(ready, { status: 'Issued', lines: [{ id: 1 }] })).toBe('AlreadyIssued');
  });

  it('مسوّدةٌ كاملةٌ وإعدادٌ جاهز ⇒ لا مانع', () => {
    expect(issueBlocker(ready, draft)).toBeNull();
  });
});

describe('معاينةُ مجموع السطر', () => {
  it('تُقرّب إلى ثلاث خانات لا أكثر', () => {
    // عرضٌ لا حساب: الرقم المُلزِم يأتي من الخادم، وهذه تمنع مفاجأةً في شاشة.
    expect(previewLineTotal(3.3333, 1)).toBe(3.333);
    expect(previewLineTotal(2, 12.5)).toBe(25);
  });

  it('مدخلٌ ليس رقماً ⇒ لا معاينة، لا NaN على الشاشة', () => {
    expect(previewLineTotal('', 5)).toBeNull();
    expect(previewLineTotal('abc', 5)).toBeNull();
    expect(previewLineTotal(1, undefined)).toBeNull();
  });
});

describe('فحصُ السطر قبل الإرسال', () => {
  it('الوصفُ مطلوب والكمّيةُ موجبة والسعرُ غيرُ سالب', () => {
    expect(lineProblems({ description: '   ', quantity: 1, unitAmount: 1 })).toEqual({ description: 'required' });
    expect(lineProblems({ description: 'x', quantity: 0, unitAmount: 1 })).toEqual({ quantity: 'positive' });
    expect(lineProblems({ description: 'x', quantity: 1, unitAmount: -1 })).toEqual({ unitAmount: 'nonNegative' });
  });

  it('سعرُ صفرٍ مقبول: سطرٌ مجّاني قرارٌ مشروع', () => {
    expect(lineProblems({ description: 'x', quantity: 1, unitAmount: 0 })).toEqual({});
  });
});

describe('فحصُ السداد قبل الإرسال', () => {
  it('ما يتجاوز المتبقّي يُقال قبل الرحلة، بسببه', () => {
    // الخادم يرفضه بـ 422، لكنّ مشغّلاً يقرأ «أكثر من المتبقّي» يصحّح رقمَه، ومشغّلاً يقرأ
    // رفضاً عامّاً يفتح بلاغاً.
    expect(paymentProblems({ amount: 120, receivedAtUtc: '2026-09-01' }, 100))
      .toEqual({ amount: 'exceedsOutstanding' });
  });

  it('المبلغُ موجبٌ والتاريخُ مطلوب', () => {
    expect(paymentProblems({ amount: 0, receivedAtUtc: '2026-09-01' }, 100)).toEqual({ amount: 'positive' });
    expect(paymentProblems({ amount: 10, receivedAtUtc: '' }, 100)).toEqual({ receivedAtUtc: 'required' });
  });

  it('سدادٌ يساوي المتبقّي تماماً مقبول', () => {
    expect(paymentProblems({ amount: 100, receivedAtUtc: '2026-09-01' }, 100)).toEqual({});
  });
});

describe('المرشّحاتُ في العنوان', () => {
  it('تُقرأ وتُكتب ذهاباً وإياباً', () => {
    const filters = { status: 'Issued', tenantId: '7', overdueOnly: true, q: 'INV001' };
    const round = filtersFromSearch(searchFromFilters(filters, 3));
    expect(round).toEqual(filters);
    expect(pageFromSearch(searchFromFilters(filters, 3))).toBe(3);
  });

  it('الصفحةُ الأولى لا تُكتب في العنوان', () => {
    expect(searchFromFilters({}, 1).toString()).toBe('');
    expect(pageFromSearch(new URLSearchParams())).toBe(1);
  });

  it('صفحةٌ غير صالحة في العنوان ⇒ الأولى، لا انهيار', () => {
    expect(pageFromSearch(new URLSearchParams('page=abc'))).toBe(1);
    expect(pageFromSearch(new URLSearchParams('page=0'))).toBe(1);
    expect(pageFromSearch(new URLSearchParams('page=-4'))).toBe(1);
  });

  it('القيمُ الفارغة لا تُرسَل، فمفتاحُ الذاكرة واحدٌ لحالتين متطابقتين', () => {
    expect(invoiceQuery({ status: '', tenantId: '', overdueOnly: false, q: '' }, 1, 20)).toEqual({
      status: undefined, tenantId: undefined, overdueOnly: undefined, search: undefined, page: 1, pageSize: 20,
    });
  });
});
