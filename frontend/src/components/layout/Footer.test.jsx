// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

// ============================================================================
// تذييل المتجر — روابط السياسات (TD-42، قرار المالك C).
//
// ما يُختبر هنا هو الفرق بين هذا القرار وما كان قبله: المرحلة 16 حذفت ستّة روابط `href="#"` لأن
// الرابط الميّت يُوهم المشتري بوجود صفحة. فالقاعدة الآن أنّ **ما يظهر يذهب إلى صفحة فعلاً**:
// النوع غير المضبوط لا يُرسم، والعمود كلّه يغيب عن متجر لم يضبط شيئاً. وهذا بعينه ما يسهل أن
// ينكسر لاحقاً بسلسلة `?? ''` في غير موضعها، فيعود الرابط الميّت من حيث أُزيل.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, vars) => (vars ? `${key}:${JSON.stringify(vars)}` : key),
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));

const store = vi.hoisted(() => ({ config: null }));
vi.mock('../../app/TenantProvider', () => ({
  useStoreConfig: () => store.config,
  useModule: () => false,
}));
vi.mock('../../app/StoreBrand', () => ({
  default: () => null,
  useStoreName: () => 'متجر',
}));

const Footer = (await import('./Footer')).default;
const { POLICY_LABELS } = await import('./Footer');

const withPolicies = (policies) => ({
  settings: {
    locale: { defaultCulture: 'ar' },
    seo: { description: {} },
    contact: { address: {}, email: null, phone: null },
    social: [],
    policies,
  },
});

const draw = (config) => {
  store.config = config;
  return render(<MemoryRouter><Footer /></MemoryRouter>);
};

describe('روابط سياسات التذييل', () => {
  it('لا عمود سياسات لمتجر لم يضبط رابطاً واحداً', () => {
    draw(withPolicies({}));
    expect(screen.queryByText('footer.policies.heading')).toBeNull();
  });

  it('المضبوط وحده يُرسم، ويذهب إلى عنوان التاجر نفسه', () => {
    draw(withPolicies({ privacy: 'https://merchant.test/privacy', faq: 'https://merchant.test/faq' }));

    expect(screen.getByText('footer.policies.heading')).toBeTruthy();
    const privacy = screen.getByRole('link', { name: 'footer.policies.privacy' });
    expect(privacy.getAttribute('href')).toBe('https://merchant.test/privacy');
    expect(privacy.getAttribute('rel')).toBe('noopener noreferrer');
    expect(screen.getByRole('link', { name: 'footer.policies.faq' })).toBeTruthy();

    // الثلاثة الأخرى غير مضبوطة ⇒ غائبة، لا روابط ميّتة.
    for (const kind of ['terms', 'returns', 'shipping']) {
      expect(screen.queryByText(POLICY_LABELS[kind])).toBeNull();
    }
  });

  it('لا رابط بلا عنوان: قيمة فارغة تُعامل كغير مضبوطة', () => {
    draw(withPolicies({ terms: '', returns: null }));
    expect(screen.queryByText('footer.policies.heading')).toBeNull();
  });

  it('الترتيب ثابت من POLICY_LABELS لا من ترتيب ردّ الخادم', () => {
    draw(withPolicies({ faq: 'https://m.test/f', privacy: 'https://m.test/p', terms: 'https://m.test/t' }));

    const names = screen.getAllByRole('link')
      .map((a) => a.textContent)
      .filter((text) => text.startsWith('footer.policies.'));
    expect(names).toEqual(['footer.policies.privacy', 'footer.policies.terms', 'footer.policies.faq']);
  });
});
