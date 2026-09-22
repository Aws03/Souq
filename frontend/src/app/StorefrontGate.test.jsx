// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes, useOutletContext } from 'react-router-dom';

// ============================================================================
// بوّابة صفحات التسوّق، و**السياق الذي كانت تمحوه** (C3 أدخلها، وC8 أمسك أثرها).
//
// العطب: `useOutlet(context)` في react-router يلفّ أبناءه بمزوّدٍ **دائماً**، بقيمة الوسيط —
// فـ`<Outlet />` بلا وسيط يمرّر `undefined` ويطمس ما وضعه التخطيط الأب. وبين `CustomerLayout`
// (الذي يضع `showToast` و`categories`) والصفحات وقعت بوّابةٌ بلا مسار، فوصلت الصفحاتِ
// `undefined`: الرئيسية و«العروض» تفكّانه فتسقطان — **واجهةُ المتجر مكسورةٌ لكلّ زائر**.
//
// **ولماذا لم يمسكه اختبارُ وحدةٍ قائم؟** لأنّ اختبارات الصفحات تركّب الصفحة تحت مزوّدٍ تكتبه
// هي (`<Outlet context={{…}} />` مباشرةً فوقها)، فلا بوّابةَ في الشجرة أصلاً. العطبُ في
// **التركيب** لا في المكوّن، فلا يُرى إلّا بتركيبٍ حقيقيّ — وهذا ما يفعله هذا الملف، ويفعله
// `storefront.spec.js` في متصفّح.
//
// ولهذا يُبنى هنا التداخلُ نفسه الذي في `App.jsx`: تخطيطٌ يضع السياق، ثمّ بوّابةٌ بلا مسار،
// ثمّ الصفحة. اختبارٌ يستدعي `StorefrontGate` وحدها لا يفحص شيئاً.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key) => key, i18n: { language: 'ar', dir: () => 'rtl' } }),
}));

const config = vi.hoisted(() => ({ value: { status: 'Active' } }));
vi.mock('./TenantProvider', () => ({ useStoreConfig: () => config.value }));

const { StorefrontGate } = await import('./StoreClosed');

const Layout = () => <Outlet context={{ showToast: 'من التخطيط', categories: [] }} />;

function Page() {
  const context = useOutletContext();
  return <p>{context === undefined ? 'لا سياق' : String(context.showToast)}</p>;
}

const renderTree = () => render(
  <MemoryRouter initialEntries={['/']}>
    <Routes>
      <Route element={<Layout />}>
        <Route element={<StorefrontGate />}>
          <Route index element={<Page />} />
        </Route>
      </Route>
    </Routes>
  </MemoryRouter>,
);

describe('بوّابة صفحات التسوّق', () => {
  it('تمرّر سياق التخطيط إلى الصفحة بدل أن تمحوه', () => {
    config.value = { status: 'Active' };
    renderTree();

    expect(screen.getByText('من التخطيط')).toBeInTheDocument();
    expect(screen.queryByText('لا سياق')).not.toBeInTheDocument();
  });

  // والبوّابة تبقى بوّابة: متجرٌ مغلق لا تصله الصفحة أصلاً.
  it('متجرٌ مغلق يرى الإشعار لا الصفحة', () => {
    config.value = { status: 'Suspended' };
    renderTree();

    expect(screen.queryByText('من التخطيط')).not.toBeInTheDocument();
    expect(screen.getByRole('heading')).toBeInTheDocument();
  });
});
