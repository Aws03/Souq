import { describe, expect, it } from 'vitest';
import {
  MAX_BASIS_POINTS, basisPointsOf, collectionReason, draftVersion, effectiveVersion, percentOf,
  profileCollects, rateProblems, versionProblems, versionsNewestFirst,
} from './taxProfiles';

// ============================================================================
// منطقُ شاشات الضريبة ([ADR-0055](0055)، قرار المالك P-06).
//
// **والاختبارُ الذي يهمّ فعلاً واحد**: أنّ «منشور» وحدها لا تكفي للجمع. إصدارٌ صحيحُ الشكل
// ومنشورٌ ونافذٌ ولم يؤكّده أحد ⇒ لا يُجمَع به شيء. لو انقلبت هذه القاعدة يوماً لَصار رقمٌ
// وجدته الهندسة في وثيقةٍ يُفرَض على مشترٍ حقيقيّ.
// ============================================================================
const version = (patch) => ({
  id: 1, version: 1, effectiveFrom: '2026-01-01T00:00:00Z', status: 'Published',
  priceMode: 'Exclusive', shippingTaxable: false, verificationState: 'Verified',
  rates: [{ code: 'standard', name: 'Standard', basisPoints: 1600, category: 'standard' }], ...patch,
});

const profile = (versions) => ({ id: 1, jurisdiction: 'TST', name: 'Test', versions });

describe('النسبة ونقاط الأساس', () => {
  it('تتحوّلان في الاتجاهين بلا فاصلةٍ عائمة في القاعدة', () => {
    expect(percentOf(1600)).toBe(16);
    expect(basisPointsOf(16)).toBe(1600);
    expect(basisPointsOf('7.5')).toBe(750);
  });

  it('كسرُ نقطةٍ يُقرَّب لا يُمرَّر', () => {
    // القاعدةُ لا تمثّل جزءاً من نقطة، وتمريرُه يُنتج رفضاً يبدو غامضاً للمشغّل.
    expect(basisPointsOf(16.005)).toBe(1601);
    expect(basisPointsOf(0)).toBe(0);
  });

  it('مدخلٌ ليس رقماً ⇒ لا قيمة', () => {
    expect(basisPointsOf('')).toBeNull();
    expect(basisPointsOf('abc')).toBeNull();
  });
});

describe('البوّابة التي يوجد الملفّ لأجلها', () => {
  it('منشورٌ ولم يتحقّق منه أحد ⇒ لا يُجمَع به', () => {
    expect(profileCollects(profile([version({ verificationState: 'Unverified' })]))).toBe(false);
    expect(profileCollects(profile([version({ verificationState: 'RequiresProfessionalConfirmation' })])))
      .toBe(false);
  });

  it('متحقَّقٌ منه ومسوّدة ⇒ لا يُجمَع: المسوّدةُ لا تنفذ على أحد', () => {
    expect(profileCollects(profile([version({ status: 'Draft', verificationState: 'Verified' })]))).toBe(false);
  });

  it('منشورٌ ومتحقَّقٌ منه معاً ⇒ يُجمَع', () => {
    expect(profileCollects(profile([version()]))).toBe(true);
  });
});

describe('الإصدار النافذ', () => {
  it('أحدثُ منشورٍ لا يتجاوز اللحظة', () => {
    const p = profile([
      version({ id: 1, version: 1, effectiveFrom: '2026-01-01T00:00:00Z' }),
      version({ id: 2, version: 2, effectiveFrom: '2026-06-01T00:00:00Z' }),
    ]);
    expect(effectiveVersion(p, '2026-03-01T00:00:00Z').version).toBe(1);
    expect(effectiveVersion(p, '2026-07-01T00:00:00Z').version).toBe(2);
  });

  it('المسوّدةُ لا تنفذ ولو كان تاريخُها في الماضي', () => {
    const p = profile([version({ status: 'Draft', effectiveFrom: '2020-01-01T00:00:00Z' })]);
    expect(effectiveVersion(p, '2026-01-01T00:00:00Z')).toBeNull();
  });

  it('إصدارٌ ينفذ لاحقاً لا يسري اليوم', () => {
    const p = profile([version({ effectiveFrom: '2027-01-01T00:00:00Z' })]);
    expect(effectiveVersion(p, '2026-01-01T00:00:00Z')).toBeNull();
  });

  it('الترتيبُ للقراءة: الأحدث أوّلاً', () => {
    const p = profile([version({ version: 1 }), version({ version: 3 }), version({ version: 2 })]);
    expect(versionsNewestFirst(p).map((v) => v.version)).toEqual([3, 2, 1]);
  });

  it('المسوّدةُ القائمة تُعرَف، وواحدةٌ على الأكثر', () => {
    expect(draftVersion(profile([version()]))).toBeNull();
    expect(draftVersion(profile([version({ status: 'Draft' })]))).not.toBeNull();
  });
});

describe('سببُ عدم الجمع', () => {
  it('أربعُ حالاتٍ بأسمائها، لا صفرٌ صامت', () => {
    const verified = profile([version()]);
    expect(collectionReason({ taxProfileId: null, collectionEnabled: true }, null)).toBe('NoProfileSelected');
    expect(collectionReason({ taxProfileId: 1, collectionEnabled: false }, verified)).toBe('CollectionDisabled');
    expect(collectionReason({ taxProfileId: 1, collectionEnabled: true }, profile([])))
      .toBe('NoEffectiveVersion');
    expect(collectionReason({ taxProfileId: 1, collectionEnabled: true },
      profile([version({ verificationState: 'Unverified' })]))).toBe('VersionNotVerified');
    expect(collectionReason({ taxProfileId: 1, collectionEnabled: true }, verified)).toBe('Collecting');
  });
});

describe('فحصُ المدخلات قبل الإرسال', () => {
  it('النسبةُ داخل مداها، والرمزُ والاسمُ مطلوبان', () => {
    expect(rateProblems({ code: 'standard', name: 'Standard', percent: 16 })).toEqual({});
    expect(rateProblems({ code: '', name: 'Standard', percent: 16 })).toEqual({ code: 'required' });
    expect(rateProblems({ code: 'x', name: 'x', percent: 101 })).toEqual({ percent: 'range' });
    expect(rateProblems({ code: 'x', name: 'x', percent: -1 })).toEqual({ percent: 'range' });
    expect(rateProblems({ code: 'x', name: 'x', percent: MAX_BASIS_POINTS / 100 })).toEqual({});
  });

  it('**ضريبةُ الشحن سؤالٌ بلا افتراض**: «لم يُجَب» مشكلةٌ لا قيمة', () => {
    // خانةُ تأشيرٍ كانت ستُجيب «لا» نيابةً عمّن لم يُجب — وهو خطأٌ بمقدار ضريبة الشحن في كل
    // طلب، في اتجاهٍ لا يظهر إلّا في تسويةٍ ضريبية. ولهذا القيمةُ المبدئية `null` لا `false`.
    const base = { effectiveFrom: '2026-01-01', priceMode: 'Exclusive', rates: [{}] };
    expect(versionProblems({ ...base, shippingTaxable: null })).toEqual({ shippingTaxable: 'required' });
    expect(versionProblems({ ...base, shippingTaxable: undefined })).toEqual({ shippingTaxable: 'required' });
    expect(versionProblems({ ...base, shippingTaxable: false })).toEqual({});
    expect(versionProblems({ ...base, shippingTaxable: true })).toEqual({});
  });

  it('عُرفُ السعر مطلوبٌ صراحةً، والإصدارُ يحتاج نسبةً واحدة', () => {
    expect(versionProblems({ effectiveFrom: '2026-01-01', priceMode: '', shippingTaxable: false, rates: [{}] }))
      .toEqual({ priceMode: 'required' });
    expect(versionProblems({ effectiveFrom: '2026-01-01', priceMode: 'Exclusive', shippingTaxable: false, rates: [] }))
      .toEqual({ rates: 'atLeastOne' });
  });
});
