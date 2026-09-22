import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  currentSearchExecution, flush, rememberSearchExecution, reportClick, reportImpression, reportView,
  resetAnalytics, setEventPoster,
} from './analytics';

// ============================================================================
// قياسُ سلوك واجهة المتجر (C9b، ADR-0050 §3).
//
// ما يُحرس هنا أربعةٌ، ولكلٍّ منها ضررٌ لو انكسر:
//   • أنّ **الظهور يُجمَّع في طلبٍ واحد**: طلبٌ لكلّ بطاقة يُغرق الشبكة ويصطدم بحدّ المعدّل،
//     فتضيع البيانات التي جُمعت لأجلها،
//   • أنّ **النقرة تُرسَل فوراً** ومعها ما جُمع: الصفحةُ على وشك التبديل، ومؤقّتٌ لن يُستأنف،
//   • أنّ **قائمةً جديدة تُرسل ما قبلها** — حدثُ الظهور يحمل قائمةً واحدة بحكم شكله، وخلطُ
//     قائمتين ينسب منتجاتِ إحداهما إلى الأخرى،
//   • وأنّ **عطباً في الإرسال لا يخرج من هذا الملفّ**: متسوّقٌ يرى خطأً سببه قياسٌ لا يراه أصلاً
//     هو أسوأُ نتيجةٍ ممكنة.
// ============================================================================
let sent;

beforeEach(() => {
  vi.useFakeTimers();
  resetAnalytics();
  sent = [];
  setEventPoster((path, body) => { sent.push({ path, body }); return Promise.resolve(); });
});

afterEach(() => {
  vi.useRealTimers();
  setEventPoster(null);
});

describe('الظهور', () => {
  it('يُجمَّع في طلبٍ واحد بدل طلبٍ لكلّ بطاقة', () => {
    reportImpression('search', 1, 1);
    reportImpression('search', 2, 2);
    reportImpression('search', 3, 3);

    expect(sent).toHaveLength(0);
    vi.runAllTimers();

    expect(sent).toHaveLength(1);
    expect(sent[0].body.listId).toBe('search');
    expect(sent[0].body.impressions).toEqual([
      { productId: 1, position: 1 }, { productId: 2, position: 2 }, { productId: 3, position: 3 },
    ]);
  });

  // تمريرٌ ذهاباً وإياباً يُركّب البطاقة مرّتين؛ الظهورُ الواحد يكفي.
  it('المنتج الواحد لا يتكرّر في الدفعة', () => {
    reportImpression('search', 1, 1);
    reportImpression('search', 1, 1);
    vi.runAllTimers();

    expect(sent[0].body.impressions).toHaveLength(1);
  });

  it('قائمةٌ جديدة تُرسل ما جُمع قبلها بدل أن تختلط به', () => {
    reportImpression('search', 1, 1);
    reportImpression('category:3', 9, 1);

    expect(sent).toHaveLength(1);
    expect(sent[0].body.listId).toBe('search');

    vi.runAllTimers();
    expect(sent).toHaveLength(2);
    expect(sent[1].body.listId).toBe('category:3');
  });

  it('لا يُرسل شيئاً بلا ظهور', () => {
    flush();
    expect(sent).toHaveLength(0);
  });

  it('بلا هويّة قائمة أو موضع لا يُقاس شيء', () => {
    reportImpression(null, 1, 1);
    reportImpression('search', 1, null);
    vi.runAllTimers();

    expect(sent).toHaveLength(0);
  });
});

describe('النقرة', () => {
  it('تُرسَل فوراً ومعها ما جُمع', () => {
    reportImpression('search', 1, 1);
    reportClick('search', 2, 2);

    expect(sent).toHaveLength(1);
    expect(sent[0].body.click).toEqual({ productId: 2, position: 2 });
    expect(sent[0].body.impressions).toEqual([{ productId: 1, position: 1 }]);
  });
});

describe('معاينة الصنف', () => {
  it('تُرسَل بمفردها فوراً', () => {
    reportView(7, { variantId: 12, listId: 'search', position: 3 });

    expect(sent).toHaveLength(1);
    expect(sent[0].body.view).toEqual({ productId: 7, variantId: 12, listId: 'search', position: 3 });
  });
});

describe('معرّف تنفيذ البحث', () => {
  it('يُحفظ ليلحق الطلبات التالية، ولا يُمحى بنتيجةٍ بلا معرّف', () => {
    expect(currentSearchExecution()).toBeNull();

    rememberSearchExecution('0199-abc');
    expect(currentSearchExecution()).toBe('0199-abc');

    // تصفّحُ فئةٍ لا يصكّ معرّفاً — ولا يجوز أن يمحو معرّف البحث الذي قاد إليها.
    rememberSearchExecution(undefined);
    rememberSearchExecution('');
    expect(currentSearchExecution()).toBe('0199-abc');
  });
});

describe('القياس لا يُفشل تصفّحاً', () => {
  it('مُرسِلٌ يرمي لا يُخرج خطأً', () => {
    setEventPoster(() => { throw new Error('الشبكة'); });

    expect(() => { reportView(1); }).not.toThrow();
  });

  it('وعدٌ مرفوض لا يُخرج خطأً', async () => {
    setEventPoster(() => Promise.reject(new Error('503')));

    expect(() => { reportView(1); }).not.toThrow();
    await Promise.resolve();
  });

  it('بلا مُرسِلٍ مضبوط لا شيء يقع', () => {
    setEventPoster(null);

    expect(() => { reportView(1); }).not.toThrow();
  });
});
