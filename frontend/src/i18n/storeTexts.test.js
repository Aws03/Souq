// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest';
import i18n, { applyStoreTexts, setStoreTexts } from './index';

// ============================================================================
// تسمياتُ المتجر كطبقةٍ فوق حزمة اللغة (C8، ADR-0062).
//
// **والعطبُ الذي يحرسه هذا الملفّ ترتيبيّ لا منطقيّ**: حزمةُ اللغة تُحمَّل من جديد عند كلّ
// تبديل، و`addResourceBundle` تكتب فوق ما سبقها — فتسمياتُ التاجر تختفي بمجرّد أن يضغط
// الزائرُ زرّ اللغة. ولذلك تُحفظ التسميات وتُعاد بعد كلّ تحميل، وهذا ما يُفحص هنا.
//
// ويُفحص معه أنّ المفتاح المنقّط **يُدمج في شجرته** لا يستبدلها: تسميةُ `store.newArrivals`
// يجب ألّا تمحو بقيّة `store.*` — وذلك ما يقع لو كُتب المفتاح مسطَّحاً.
// ============================================================================
beforeEach(async () => {
  setStoreTexts(null);
  await i18n.changeLanguage('ar');
  i18n.addResourceBundle('ar', 'translation', {
    store: { newArrivals: 'وصل حديثاً', allProducts: 'كل المنتجات' },
  }, true, true);
});

describe('تسميات المتجر', () => {
  it('تُطبَّق فوق النصّ الأصليّ', () => {
    setStoreTexts({ 'store.newArrivals': { ar: 'مجموعاتنا' } });

    expect(i18n.t('store.newArrivals')).toBe('مجموعاتنا');
  });

  it('لا تمحو بقيّة مفاتيح القسم نفسه', () => {
    setStoreTexts({ 'store.newArrivals': { ar: 'مجموعاتنا' } });

    expect(i18n.t('store.allProducts')).toBe('كل المنتجات');
  });

  // **الحارسُ الأهمّ**: إعادةُ تحميل الحزمة تكتب فوق الطبقة، فتُعاد صراحةً.
  it('تبقى بعد إعادة تحميل حزمة اللغة فوقها', () => {
    setStoreTexts({ 'store.newArrivals': { ar: 'مجموعاتنا' } });

    i18n.addResourceBundle('ar', 'translation', {
      store: { newArrivals: 'وصل حديثاً', allProducts: 'كل المنتجات' },
    }, true, true);
    expect(i18n.t('store.newArrivals')).toBe('وصل حديثاً', 'الحزمة كتبت فوقها — وهذا هو العطب');

    applyStoreTexts('ar');
    expect(i18n.t('store.newArrivals')).toBe('مجموعاتنا');
  });

  it('لغةٌ لا تسمية لها فيها تُبقي النصّ الأصليّ', () => {
    setStoreTexts({ 'store.newArrivals': { en: 'Our collections' } });

    expect(i18n.t('store.newArrivals')).toBe('وصل حديثاً');
  });

  it('نصٌّ فارغ أو غير نصّي يُتجاهَل بدل أن يُفرغ الزرّ', () => {
    setStoreTexts({ 'store.newArrivals': { ar: '' }, 'store.allProducts': { ar: 42 } });

    expect(i18n.t('store.newArrivals')).toBe('وصل حديثاً');
    expect(i18n.t('store.allProducts')).toBe('كل المنتجات');
  });

  it('مسحُ التسميات يعيد النصّ الأصليّ بعد إعادة التحميل', () => {
    setStoreTexts({ 'store.newArrivals': { ar: 'مجموعاتنا' } });
    setStoreTexts(null);

    i18n.addResourceBundle('ar', 'translation', { store: { newArrivals: 'وصل حديثاً' } }, true, true);
    applyStoreTexts('ar');

    expect(i18n.t('store.newArrivals')).toBe('وصل حديثاً');
  });
});
