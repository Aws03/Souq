// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// كشف الافتتاح: العقد السلوكي لا البكسلات.
// أهمّ ما يُختبر أنه لا يصير سجناً: يُتخطّى، ولا يتكرّر، ويُزال من الشجرة بعده فلا يبتلع نقرة.
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key }) }));

const config = vi.hoisted(() => ({ value: null }));
vi.mock('../../app/TenantProvider', () => ({ useStoreConfig: () => config.value }));
vi.mock('../../app/StoreBrand', () => ({ useStoreName: () => 'Test Store' }));

const OpeningExperience = (await import('./OpeningExperience')).default;

const withOpening = (opening) => ({ settings: { branding: { opening } } });

const setMotionPreference = (reduce) => {
  window.matchMedia = vi.fn().mockImplementation((query) => ({
    matches: query.includes('reduce') ? reduce : false,
    addEventListener: vi.fn(), removeEventListener: vi.fn(),
  }));
};

beforeEach(() => {
  sessionStorage.clear();
  setMotionPreference(false);
  window.history.pushState({}, '', '/');
  config.value = withOpening({ enabled: true, style: 'doors' });
});

describe('متى يُعرض', () => {
  it('متجر فعّله، زائر أوّل مرّة على الواجهة', () => {
    render(<OpeningExperience />);
    expect(screen.getByTestId('opening-experience')).toBeInTheDocument();
  });

  it('متجر لم يفعّله لا يرى شيئاً — والافتراضي عدم التفعيل', () => {
    config.value = withOpening({ enabled: false });
    const { container } = render(<OpeningExperience />);
    expect(container).toBeEmptyDOMElement();

    config.value = { settings: { branding: {} } };
    expect(render(<OpeningExperience />).container).toBeEmptyDOMElement();
  });

  it('تفضيل تقليل الحركة يمنعه تماماً', () => {
    setMotionPreference(true);
    const { container } = render(<OpeningExperience />);
    expect(container).toBeEmptyDOMElement();
  });

  it('رابط عميق يفتح ما طُلب بلا ستارة', () => {
    window.history.pushState({}, '', '/products/blue-shirt');
    const { container } = render(<OpeningExperience />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe('لا يصير سجناً', () => {
  it('زرّ التخطّي يُنهيه فوراً', async () => {
    render(<OpeningExperience />);

    await userEvent.click(screen.getByRole('button', { name: 'store.skipIntro' }));

    expect(screen.queryByTestId('opening-experience')).toBeNull();
  });

  it('Escape يُنهيه', async () => {
    render(<OpeningExperience />);

    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(screen.queryByTestId('opening-experience')).toBeNull());
  });

  it('يُزال من الشجرة بعده فلا يبتلع النقرات', async () => {
    render(<OpeningExperience />);
    await userEvent.click(screen.getByRole('button', { name: 'store.skipIntro' }));

    // لا طبقة شفّافة باقية فوق الصفحة.
    expect(document.querySelector('[data-testid="opening-experience"]')).toBeNull();
  });

  it('لا يتكرّر في الجلسة نفسها', () => {
    const first = render(<OpeningExperience />);
    expect(first.getByTestId('opening-experience')).toBeInTheDocument();
    first.unmount();

    const second = render(<OpeningExperience />);
    expect(second.container).toBeEmptyDOMElement();
  });
});

describe('الإتاحة', () => {
  it('الزخرفة وحدها مخفيّة عن قارئ الشاشة، لا زرّ الخروج', () => {
    // طبقةٌ تُخفي زرّ التخطّي معها تصير سجناً لمن لا يرى الرسم أصلاً.
    const { container } = render(<OpeningExperience />);

    expect(container.querySelectorAll('[aria-hidden="true"]').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'store.skipIntro' })).toBeInTheDocument();
  });

  it('التركيز يذهب إلى زرّ التخطّي فور ظهوره', () => {
    render(<OpeningExperience />);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'store.skipIntro' }));
  });
});
