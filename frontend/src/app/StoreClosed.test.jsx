// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

// ============================================================================
// بوّابة صفحات التسوّق (C3، قرار المالك C-17 = B).
//
// ما يُختبر هنا هو **ألّا تُرسَم صفحة تسوّق لمتجر مغلق**، وأن يُرسم الإشعار مكانها برسالة حالته.
// وهو العطب الأسهل رجوعاً: البوّابة مسارٌ بلا مسار يغلّف شجرةً كاملة، فإضافةُ مسارٍ جديد **خارجها**
// سهوٌ لا يُرى في مراجعة — ولذلك يفحص الاختبار الشجرة كما تُركَّب فعلاً، لا الدالة وحدها.
//
// وحدودُ ما يبقى عاملاً مقصودة كلّها، ومطابقة لما يسمح به الخادم بالحالة نفسها.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key) => key,
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));

const store = vi.hoisted(() => ({ config: null }));
vi.mock('./TenantProvider', () => ({ useStoreConfig: () => store.config }));

const { StorefrontGate, StoreClosedNotice } = await import('./StoreClosed');

const config = (status) => ({ status, settings: { displayName: { ar: 'متجر أ' }, locale: { defaultCulture: 'ar' } } });

const draw = (status, path = '/') => {
  store.config = status === undefined ? null : config(status);
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/track/:token" element={<p>tracking-page</p>} />
        <Route element={<StorefrontGate />}>
          <Route path="/" element={<p>shopping-page</p>} />
          <Route path="/cart" element={<p>cart-page</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
};

describe('بوّابة صفحات التسوّق', () => {
  it('المتجر الفعّال يرى صفحاته', () => {
    draw('Active');
    expect(screen.getByText('shopping-page')).toBeTruthy();
  });

  it('إعداد بلا حالة (خادم أقدم) يُعتبر مفتوحاً، فلا تُحجب صفحة بالخطأ', () => {
    draw(undefined);
    expect(screen.getByText('shopping-page')).toBeTruthy();
  });

  it.each(['Suspended', 'Provisioning', 'Archived'])('المتجر %s لا يرى صفحة تسوّق واحدة', (status) => {
    draw(status, '/cart');
    expect(screen.queryByText('cart-page')).toBeNull();
    expect(screen.getByText(`storeClosed.${status.toLowerCase()}.title`)).toBeTruthy();
  });

  it('وتتبّع الطلب خارج البوّابة عمداً: يعمل والمتجر موقوف', () => {
    draw('Suspended', '/track/abc');
    expect(screen.getByText('tracking-page')).toBeTruthy();
    expect(screen.queryByText('storeClosed.suspended.title')).toBeNull();
  });

  it('والإشعار يحمل هويّة المتجر ورابط دخول لصاحبه', () => {
    store.config = config('Suspended');
    render(<MemoryRouter><StoreClosedNotice /></MemoryRouter>);
    expect(screen.getByText('متجر أ')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'storeClosed.signIn' }).getAttribute('href')).toBe('/login');
  });
});
