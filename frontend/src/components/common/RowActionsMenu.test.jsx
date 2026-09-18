// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// قائمة إجراءات الصفّ: العقد الذي يهمّ هنا ليس المظهر بل **أنّها تبقى مفتوحة حتى يُغلقها المستخدم**.
//
// العطل الذي تحرس منه (وُجد في M18 على حزمةٍ حقيقية، لا في الوحدة): كانت القائمة تُغلق على أي حدث
// تمرير. و`html { scroll-behavior: smooth }` يجعل نقل عنصرٍ إلى الرؤية تمريراً يستمرّ عشرات الأحداث
// بعد وقوعه — فمستخدم لوحة مفاتيح يصل بـ Tab إلى زرّ صفٍّ تحت الطيّة (فيُمرّره المتصفّح إليه سلساً)
// ثمّ يضغط Enter، كانت قائمته تُفتح وتُغلق فوراً بأحداث ذلك التمرير. قيس: 32 حدثاً، والقائمة صفر.
// أي أنّ الزرّ كان غير قابل للاستعمال بلوحة المفاتيح على أي صفٍّ تحت الطيّة.
// ============================================================================
const { default: RowActionsMenu } = await import('./RowActionsMenu');

vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (k) => k }) }));

const actions = [
  { label: 'تعديل', onClick: vi.fn() },
  { label: 'حذف', onClick: vi.fn(), variant: 'danger' },
];

describe('RowActionsMenu', () => {
  it('تُفتح بلوحة المفاتيح وتُعلن حالتها', async () => {
    const user = userEvent.setup();
    render(<RowActionsMenu actions={actions} label="إجراءات الصفّ" />);
    const trigger = screen.getByRole('button', { name: 'إجراءات الصفّ' });

    expect(trigger).toHaveAttribute('aria-haspopup', 'menu');
    expect(trigger).toHaveAttribute('aria-expanded', 'false');

    trigger.focus();
    await user.keyboard('{Enter}');

    expect(screen.getByRole('menu')).toBeInTheDocument();
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getAllByRole('menuitem')).toHaveLength(2);
  });

  it('التمرير لا يُغلقها — يُعيد موضعها', async () => {
    const user = userEvent.setup();
    render(<RowActionsMenu actions={actions} label="إجراءات الصفّ" />);
    await user.click(screen.getByRole('button', { name: 'إجراءات الصفّ' }));
    expect(screen.getByRole('menu')).toBeInTheDocument();

    // ما يفعله التمرير السلس: أحداث متتابعة بعد فتح القائمة مباشرة.
    for (let i = 0; i < 12; i += 1) window.dispatchEvent(new Event('scroll'));
    await new Promise((resolve) => requestAnimationFrame(resolve));

    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('تُغلق بـ Escape وبنقرة خارجية، ويُنفَّذ الإجراء المختار', async () => {
    const user = userEvent.setup();
    render(
      <>
        <button type="button">خارجها</button>
        <RowActionsMenu actions={actions} label="إجراءات الصفّ" />
      </>,
    );
    const trigger = screen.getByRole('button', { name: 'إجراءات الصفّ' });

    await user.click(trigger);
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();

    await user.click(trigger);
    await user.click(screen.getByRole('button', { name: 'خارجها' }));
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();

    await user.click(trigger);
    await user.click(screen.getByRole('menuitem', { name: 'حذف' }));
    expect(actions[1].onClick).toHaveBeenCalledOnce();
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });
});
