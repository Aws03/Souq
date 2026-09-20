// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// الترقيم: حدوده حقيقية لا زينة.
//
// سقط هذا في شاشة أثر البحث: كانت وحدها تمرّر `pageSize`/`total` بدل `totalPages`، فيصل
// `totalPages` غير معرّف. وكل مقارنة مع `undefined` خطأ، فـ `page >= totalPages` خطأ أبداً
// و"التالي" لا يتعطّل أبداً — يمضي التاجر إلى صفحة ٢٦ من جدولٍ صفحتين، كلّها فارغة، والعدّاد
// يقول "صفحة ٢٦ من" بلا رقم. لم يكن عطلاً مرئياً في الاختبارات لأن لا شيء كان يفحص الحدّ.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key, values) => (values ? `${key}:${JSON.stringify(values)}` : key) }),
}));
const Pagination = (await import('./Pagination')).default;

const next = () => screen.getByRole('button', { name: /common\.next/ });
const previous = () => screen.getByRole('button', { name: /common\.previous/ });

describe('Pagination', () => {
  it('يعطّل "التالي" على الصفحة الأخيرة و"السابق" على الأولى', () => {
    const { unmount } = render(<Pagination page={1} totalPages={3} onChange={vi.fn()} />);
    expect(previous()).toBeDisabled();
    expect(next()).toBeEnabled();
    unmount();

    render(<Pagination page={3} totalPages={3} onChange={vi.fn()} />);
    expect(next()).toBeDisabled();
    expect(previous()).toBeEnabled();
  });

  it('ينتقل بالصفحة التالية والسابقة', async () => {
    const onChange = vi.fn();
    render(<Pagination page={2} totalPages={5} onChange={onChange} />);

    await userEvent.click(next());
    expect(onChange).toHaveBeenCalledWith(3);

    await userEvent.click(previous());
    expect(onChange).toHaveBeenCalledWith(1);
  });

  it('لا يظهر أصلاً حين لا يوجد ما يُرقَّم', () => {
    const { container, unmount } = render(<Pagination page={1} totalPages={1} onChange={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
    unmount();

    const zero = render(<Pagination page={1} totalPages={0} onChange={vi.fn()} />);
    expect(zero.container).toBeEmptyDOMElement();
  });

  // الحارس نفسه: استدعاءٌ ناقص العدد يجب أن **يختفي**، لا أن يصير ترقيمةً بلا نهاية.
  // فغيابٌ ظاهر يكتشفه أوّل من ينظر، أمّا "التالي" الذي لا يتعطّل فيمشي بالمستخدم إلى الفراغ.
  it.each([[undefined], [null], [NaN]])('لا يبني ترقيمة بلا نهاية حين يغيب العدد (%s)', (totalPages) => {
    const { container } = render(<Pagination page={26} totalPages={totalPages} onChange={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });
});
