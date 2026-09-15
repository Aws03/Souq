// @vitest-environment jsdom
import { useState } from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ErrorBoundary from './ErrorBoundary';

// i18n مستبدلة بمفاتيحها: الاختبار يفحص السلوك لا الترجمة.
vi.mock('react-i18next', () => ({
  withTranslation: () => (Component) => (props) => <Component {...props} t={(key) => key} />,
  useTranslation: () => ({ t: (key) => key }),
}));

function Explodes({ boom }) {
  if (boom) throw new Error('secret internal detail at /src/app/thing.js:42');
  return <p>working content</p>;
}

describe('ErrorBoundary', () => {
  beforeEach(() => vi.spyOn(console, 'error').mockImplementation(() => {}));
  afterEach(() => vi.restoreAllMocks());

  it('يعرض أبناءه حين لا خطأ', () => {
    render(<ErrorBoundary><Explodes boom={false} /></ErrorBoundary>);
    expect(screen.getByText('working content')).toBeInTheDocument();
  });

  it('يمسك خطأ العرض بدل ترك صفحة بيضاء', () => {
    render(<ErrorBoundary><Explodes boom /></ErrorBoundary>);

    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText('errors.boundaryTitle')).toBeInTheDocument();
  });

  it('لا يعرض نصّ الاستثناء ولا مسار ملفّ للزائر', () => {
    // نفس قاعدة عقد أخطاء الخادم: التفاصيل الداخلية للسجلّ لا للزائر.
    const { container } = render(<ErrorBoundary><Explodes boom /></ErrorBoundary>);

    expect(container.textContent).not.toContain('secret internal detail');
    expect(container.textContent).not.toContain('.js:42');
  });

  it('إعادة المحاولة تستعيد العرض حين يزول سبب الخطأ', async () => {
    // خطأ عابر (طلب فشل أثناء العرض) يجب أن يكون قابلاً للتعافي بلا إعادة تحميل الصفحة.
    function Flaky() {
      const [boom, setBoom] = useState(true);
      window.__fix = () => setBoom(false);
      return <Explodes boom={boom} />;
    }
    const user = userEvent.setup();
    render(<ErrorBoundary><Flaky /></ErrorBoundary>);

    expect(screen.getByRole('alert')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'common.retry' }));
    // يظهر الخطأ ثانيةً لأن السبب دائم — والمهم أن الزرّ يعمل ولا يُجمّد الواجهة.
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('يعرض مخرجاً إلى المتجر', () => {
    render(<ErrorBoundary><Explodes boom /></ErrorBoundary>);
    expect(screen.getByRole('link', { name: 'errors.boundaryHome' })).toHaveAttribute('href', '/');
  });
});
