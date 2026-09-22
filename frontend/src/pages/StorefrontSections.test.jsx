// @vitest-environment jsdom
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { withQueryClient } from '../test/queryWrapper';
import { DEFAULT_SECTIONS } from './Storefront';

// ============================================================================
// أقسام الرئيسية كوصفٍ مرتَّب (C8، ADR-0060).
//
// ما يُحرس هنا أربعةُ أشياء، كلٌّ منها يقع على متجرٍ حقيقيّ لو انكسر:
//   • أنّ **الترتيب يُطاع** — وهو كلُّ ما اشتراه التاجر بهذه الميزة،
//   • أنّ **القسم المُطفأ يغيب** ولا يبقى مرسوماً بصمت،
//   • أنّ **الكتالوج يُرسم حتى لو غاب عن الوصف** — واجهةٌ تثق بالخادم وحده تعرض صفحةً بلا
//     منتجات لو وصلها وصفٌ ناقص من نسخةٍ أقدم،
//   • وأنّ **ما قبل وصول الإعداد** يُرسم بالترتيب الافتراضي لا بصفحةٍ ناقصة، فلا وميض.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key) => key, i18n: { language: 'ar', dir: () => 'rtl' } }),
}));

vi.mock('../components/store/Hero', () => ({ default: () => <div data-section="hero" /> }));
vi.mock('../components/store/FeaturedProduct', () => ({ default: () => <div data-section="featured" /> }));
vi.mock('../components/store/ProductSection', () => ({
  default: ({ title }) => <div data-section={title === 'nav.offers' ? 'offers' : 'newArrivals'} />,
}));
vi.mock('../components/catalog/Catalog', () => ({ default: () => <div data-catalog /> }));

const Storefront = (await import('./Storefront')).default;

const draw = (sections, url = '/') => {
  render(withQueryClient(
    <MemoryRouter initialEntries={[url]}>
      <Storefront categories={[]} onAdded={() => {}} refreshKey={0} sections={sections}
        newArrivals={[]} bestSellers={[]} offers={[]} />
    </MemoryRouter>,
  ));
  return [...document.querySelectorAll('[data-section], #catalog')]
    .map((el) => el.getAttribute('data-section') ?? 'catalog');
};

describe('ترتيب أقسام الرئيسية', () => {
  it('يُرسم بالترتيب الذي يقوله الخادم', () => {
    expect(draw(['offers', 'hero', 'catalog'])).toEqual(['offers', 'hero', 'catalog']);
  });

  it('الترتيب المعكوس يُرسم معكوساً — لا ترتيبَ مخبوءاً في الصفحة', () => {
    const reversed = [...DEFAULT_SECTIONS].reverse();
    expect(draw(reversed)).toEqual(reversed);
  });

  it('القسم الغائب عن الوصف لا يُرسم', () => {
    expect(draw(['hero', 'catalog'])).toEqual(['hero', 'catalog']);
  });

  // الخادم يمنع إطفاء الكتالوج؛ وهذه هي الحزام الثاني، لنسخةِ واجهةٍ تسبق ذلك المنع.
  it('الكتالوج يُرسم حتى لو غاب عن الوصف', () => {
    expect(draw(['hero'])).toEqual(['hero', 'catalog']);
  });

  // رسمتان في اختبارٍ واحد تتشاركان المستند، فيُقرأ الناتجُ مضاعفاً — اختباران لا واحد.
  it('قبل وصول الإعداد (undefined) يُرسم الترتيب الافتراضي كاملاً', () => {
    expect(draw(undefined)).toEqual(DEFAULT_SECTIONS);
  });

  it('وصفٌ فارغ يُرسم بالترتيب الافتراضي أيضاً', () => {
    expect(draw([])).toEqual(DEFAULT_SECTIONS);
  });

  // نوعٌ لا تعرفه هذه النسخة يُتخطّى، ولا تسقط الصفحة معه.
  it('نوعٌ مجهول يُتخطّى ولا يكسر الصفحة', () => {
    expect(draw(['hero', 'carousel', 'catalog'])).toEqual(['hero', 'catalog']);
  });

  // سلوكٌ يسبق هذه المرحلة ويجب أن يبقى: الفلترةُ تجعل الصفحة قائمةَ كتالوجٍ نظيفة.
  it('مع فلترٍ نشط يُرسم الكتالوج وحده مهما قال الترتيب', () => {
    expect(draw(DEFAULT_SECTIONS, '/?cats=3')).toEqual(['catalog']);
  });
});

// ============================================================================
// والنسخةُ الاحتياطية في الواجهة تطابق النطاق. هي موجودةٌ للنافذة السابقة لوصول الإعداد وحدها،
// لكنّ نسخةً ثانية تفترق يوماً — و`styles.darkTokens.test.js` موجودٌ لأنّ قسمةً مشابهة انفرطت
// مرّةً وبقيت. يُقرأ النطاق لا تُنسخ قيمُه.
// ============================================================================
describe('الافتراضي في الواجهة يطابق النطاق', () => {
  it('DEFAULT_SECTIONS هو StoreSections.Types بترتيبه', () => {
    const cs = readFileSync(resolve(process.cwd(), '../src/Souq.Domain/Platform/StoreSettings.cs'), 'utf8');
    const line = cs.match(/public static readonly IReadOnlyList<string> Types = \[([^\]]+)\]/);
    expect(line, 'StoreSections.Types غير موجودة في النطاق').toBeTruthy();

    const constants = line[1].split(',').map((s) => s.trim());
    const values = constants.map((name) => cs.match(new RegExp(`public const string ${name} = "([^"]+)"`))?.[1]);

    expect(values).toEqual(DEFAULT_SECTIONS);
  });
});
