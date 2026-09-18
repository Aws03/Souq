import { describe, it, expect } from 'vitest';
import {
  TONES, VOCABULARY_NAMES, flagTone, statusTone, stockTone, valuesOf,
} from './statusTone';

// ============================================================================
// المفردة الواحدة (TD-28، M10). ما يُختبر هنا ليس "هل أُعيدت النغمة الصحيحة" فقط — بل **التناقضات
// التي كانت موجودة، مُثبَّتةً مُغلقةً**: قيمةٌ واحدة لا تُلوَّن لونين، ومعنًى واحد لا يُلوَّن لونين.
// لا اختبار كان يمسّ أيّ طبقة حالة قبل هذه المرحلة (فُحِص: صفر إشارة في كل ملفّات الاختبار)، فكان
// كل هذا التفاوت غير محروس.
// ============================================================================
describe('التناقضات التي كانت', () => {
  it('Succeeded نغمةٌ واحدة في الدفعة والاسترداد', () => {
    // كانت 'paid' للدفعة و'delivered' للاسترداد، في سطرين متجاورين من ملفّ واحد — وتُعرضان في
    // الدرج نفسه على بُعد أسطر، فيقرأ التاجر الكلمة نفسها بلونين.
    expect(statusTone('payment', 'Succeeded')).toBe(statusTone('refund', 'Succeeded'));
    expect(statusTone('payment', 'Succeeded')).toBe('success');
  });

  it('"مُفعَّل" نغمةٌ واحدة في كل الجداول', () => {
    // كانت 'paid' (أزرق) في الكوبونات وطرق الشحن و'delivered' (أخضر) في الفئات والمتغيّرات.
    expect(flagTone(true)).toBe('success');
    expect(flagTone(false)).toBe('danger');
    // والأخضر نفسه هو ما يعني "قائم" في المفردات المسمّاة، فلا يفترق العلَم عنها.
    expect(flagTone(true)).toBe(statusTone('product', 'Active'));
    expect(flagTone(true)).toBe(statusTone('customer', 'Active'));
    expect(flagTone(true)).toBe(statusTone('store', 'Active'));
  });

  it('invited نغمةٌ واحدة — الدالّة واحدة فلا يجوز أن تُقرأ بلونين', () => {
    // كانت عنبريّة في شاشة الفريق وزرقاء في حسابات المنصّة، من `accountState` نفسها.
    expect(statusTone('account', 'invited')).toBe('warning');
    expect(statusTone('account', 'active')).toBe('success');
    expect(statusTone('account', 'disabled')).toBe('danger');
  });
});

describe('المفردات', () => {
  it('كل قيمة في كل مفردة تُعيد نغمةً معروفة', () => {
    for (const vocabulary of VOCABULARY_NAMES) {
      const values = valuesOf(vocabulary);
      expect(values.length, `مفردة ${vocabulary} فارغة`).toBeGreaterThan(0);
      for (const value of values) {
        expect(TONES, `${vocabulary}.${value}`).toContain(statusTone(vocabulary, value));
      }
    }
  });

  it('دورة حياة الطلب تُقرأ تدرّجاً: انتظار ← جارٍ ← تمّ ← أُلغي', () => {
    expect(['Pending', 'Paid', 'Shipped', 'Delivered', 'Cancelled'].map((s) => statusTone('order', s)))
      .toEqual(['warning', 'info', 'info', 'success', 'danger']);
  });

  it('المحايدة مستعملة فعلاً — لا عضو ميّت في المفردة', () => {
    expect(statusTone('readiness', 'blocked')).toBe('neutral');
  });
});

describe('ما لا تعرفه المفردة', () => {
  it('قيمة مجهولة محايدة لا خطأ ولا لون مخترع', () => {
    // حالةٌ جديدة من الخادم لا تُسقط شاشة، ولا تُلوَّن أخضر أو أحمر بالحدس.
    expect(statusTone('order', 'Refunded')).toBe('neutral');
    expect(statusTone('order', undefined)).toBe('neutral');
    expect(statusTone('order', null)).toBe('neutral');
    expect(statusTone('nope', 'Active')).toBe('neutral');
  });

  it('القيم تُقرأ بحرفها: لا تطبيع ضمني يخفي قيمة جديدة', () => {
    expect(statusTone('order', 'Delivered')).toBe('success');
    expect(statusTone('order', 'delivered')).toBe('neutral');
  });
});

describe('مستوى المخزون', () => {
  it('مقياس ترتيبي على الحدّ نفسه الذي يستعمله الخادم', () => {
    expect(stockTone({ available: 0, lowStockThreshold: 5 })).toBe('danger');
    expect(stockTone({ available: 5, lowStockThreshold: 5 })).toBe('danger');  // الحدّ نفسه منخفض
    expect(stockTone({ available: 6, lowStockThreshold: 5 })).toBe('warning');
    expect(stockTone({ available: 10, lowStockThreshold: 5 })).toBe('warning'); // ضِعف الحدّ ما زال تحذيراً
    expect(stockTone({ available: 11, lowStockThreshold: 5 })).toBe('success');
  });

  it('حدّ تنبيه صفر: أيّ متاح موجب وفير، والصفر منخفض', () => {
    expect(stockTone({ available: 0, lowStockThreshold: 0 })).toBe('danger');
    expect(stockTone({ available: 1, lowStockThreshold: 0 })).toBe('success');
  });
});
