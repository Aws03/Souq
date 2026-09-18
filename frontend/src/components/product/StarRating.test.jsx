// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expectNoViolations } from '../../test/axe';

// ============================================================================
// ما يجعل خمس أيقونات **تقييماً** هو ما لا يُرى: الدور، والحالة المختارة، وما يُعلَن.
// هذان عقدان مختلفان (M19) — عرضٌ يُقرأ قيمةً واحدة، واختيارٌ يُقرأ مجموعة اختيارات.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values?.n !== undefined ? `${values.n} of 5` : key),
    i18n: { dir: () => 'ltr', language: 'en', exists: () => true },
  }),
}));

const StarRating = (await import('./StarRating')).default;

describe('StarRating — العرض', () => {
  it('يُعلَن قيمةً واحدة، لا خمسة أزرار معطّلة', async () => {
    const { container } = render(<StarRating value={4} />);

    expect(screen.getByRole('img', { name: '4 of 5' })).toBeInTheDocument();
    // النجوم نفسها خارج شجرة الإتاحة: زخرفةٌ تحمل القيمة التي قالها الاسم.
    expect(screen.queryAllByRole('button')).toHaveLength(0);
    await expectNoViolations(container);
  });

  it('المتوسّط الكسري يُقرَّب في الاسم كما يُقرَّب في الرسم', () => {
    render(<StarRating value={3.6} />);
    expect(screen.getByRole('img', { name: '4 of 5' })).toBeInTheDocument();
  });
});

describe('StarRating — الاختيار', () => {
  it('مجموعة اختيارات تُعلن المختار منها', async () => {
    const { container } = render(<StarRating value={3} onChange={vi.fn()} />);

    expect(screen.getByRole('radiogroup')).toBeInTheDocument();
    const radios = screen.getAllByRole('radio');
    expect(radios).toHaveLength(5);
    expect(radios[2]).toBeChecked();
    expect(radios[0]).not.toBeChecked();
    await expectNoViolations(container);
  });

  it('tabindex متجوّل: نقطة دخول واحدة للمجموعة', () => {
    render(<StarRating value={4} onChange={vi.fn()} />);
    const radios = screen.getAllByRole('radio');

    expect(radios.filter((r) => r.getAttribute('tabindex') === '0')).toHaveLength(1);
    expect(radios[3]).toHaveAttribute('tabindex', '0');
  });

  it('بلا اختيارٍ بعد، النجمة الأولى هي محطّة Tab', () => {
    render(<StarRating value={0} onChange={vi.fn()} />);
    const radios = screen.getAllByRole('radio');

    expect(radios[0]).toHaveAttribute('tabindex', '0');
    expect(radios.some((r) => r.getAttribute('aria-checked') === 'true')).toBe(false);
  });

  it('الأسهم تُحرّك الاختيار، وHome/End يبلغان الطرفين', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<StarRating value={3} onChange={onChange} />);
    const radios = screen.getAllByRole('radio');
    radios[2].focus();

    await user.keyboard('{ArrowRight}');
    expect(onChange).toHaveBeenLastCalledWith(4);

    await user.keyboard('{ArrowLeft}');
    expect(onChange).toHaveBeenLastCalledWith(2);

    await user.keyboard('{Home}');
    expect(onChange).toHaveBeenLastCalledWith(1);

    await user.keyboard('{End}');
    expect(onChange).toHaveBeenLastCalledWith(5);
  });

  it('النقر يختار النجمة المنقورة', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<StarRating value={0} onChange={onChange} />);

    await user.click(screen.getByRole('radio', { name: '5 of 5' }));
    expect(onChange).toHaveBeenCalledWith(5);
  });
});
