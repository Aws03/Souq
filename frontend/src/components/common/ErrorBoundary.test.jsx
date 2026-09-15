// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ErrorBoundary from './ErrorBoundary';

// i18n مستبدلة بمفاتيحها: الاختبار يفحص السلوك لا الترجمة.
vi.mock('react-i18next', () => ({
  withTranslation: () => (Component) => {
    const Translated = (props) => <Component {...props} t={(key) => key} />;
    Translated.displayName = 'Translated';
    return Translated;
  },
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

  it('زرّ إعادة المحاولة يعيد العرض ولا يُجمّد الصفحة', async () => {
    // السبب هنا دائم، فالخطأ يعود — المُختبَر أن الزرّ يعيد تركيب الأبناء فعلاً بدل أن يبقى
    // الحدّ عالقاً على حالته الأولى إلى أن يُعاد تحميل الصفحة.
    const user = userEvent.setup();
    render(<ErrorBoundary><Explodes boom /></ErrorBoundary>);

    expect(screen.getByRole('alert')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'common.retry' }));
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('يعرض مخرجاً إلى المتجر', () => {
    render(<ErrorBoundary><Explodes boom /></ErrorBoundary>);
    expect(screen.getByRole('link', { name: 'errors.boundaryHome' })).toHaveAttribute('href', '/');
  });
});
