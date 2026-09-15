// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { withQueryClient } from '../test/queryWrapper';

// ============================================================================
// اختبار "هل تُعرَض أصلاً؟" لصفحتين كانتا تسقطان وقت التشغيل وحده.
//
// كلتاهما نادت usePageMetadata بـt قبل سطر تعريفها — منطقة الموت المؤقّت لـconst، أي
// ReferenceError لحظة الدخول. البناء يمرّ، والأنواع تمرّ، ولا اختبار كان يفتحهما. المدقّق
// أمسكها بقاعدة no-use-before-define، وهذا يمنع عودتها من باب آخر: صفحة تسقط تفشل هنا.
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key, i18n: { dir: () => 'ltr', language: 'en' } }) }));
vi.mock('../app/usePageMetadata', () => ({ usePageMetadata: () => {} }));
vi.mock('../components/catalog/Catalog', () => ({ default: () => <p>catalog</p> }));
vi.mock('../components/store/Hero', () => ({ default: () => <p>hero</p> }));
vi.mock('../components/product/ProductCard', () => ({ default: () => <p>card</p> }));
vi.mock('../context/WishlistContext', () => ({ useWishlist: () => ({ items: [] }) }));
vi.mock('../context/ToastContext', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

const client = vi.hoisted(() => ({ getProducts: vi.fn() }));
vi.mock('../api/client', () => ({ api: client }));

const Offers = (await import('./Offers')).default;
const Wishlist = (await import('./Wishlist')).default;
const Store = (await import('./Store')).default;

const Layout = () => <Outlet context={{ showToast: vi.fn(), refreshKey: 0, categories: [] }} />;

function renderPage(element) {
  return render(withQueryClient(
    <MemoryRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={element} />
        </Route>
      </Routes>
    </MemoryRouter>
  ));
}

describe('storefront pages render at all', () => {
  it('صفحة العروض', () => {
    renderPage(<Offers />);
    expect(screen.getByRole('heading', { name: 'offers.title' })).toBeInTheDocument();
  });

  it('صفحة المفضّلة', () => {
    renderPage(<Wishlist />);
    expect(screen.getByRole('heading', { name: 'wishlist.title' })).toBeInTheDocument();
  });

  it('الصفحة الرئيسية', () => {
    client.getProducts.mockResolvedValue({ items: [], totalCount: 0, totalPages: 1, pageNumber: 1 });
    renderPage(<Store />);
    expect(screen.getByText('hero')).toBeInTheDocument();
    expect(screen.getByText('catalog')).toBeInTheDocument();
  });
});
