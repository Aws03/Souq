// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// تأكيد ثم تنفيذ: الإجراء لا يُستدعى قبل التأكيد، والحوار يُغلق بالنجاح وحده، ورفض الخادم يبقى فيه.
// وحوار فوق درج: Escape يغلق الأعلى وحده — لا الدرج الذي خلفه.
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key }) }));
const { useConfirmAction } = await import('./useConfirmAction');
const Drawer = (await import('./Drawer')).default;

function Harness({ action, inDrawer = false, onDrawerClose = () => {} }) {
  const confirmation = useConfirmAction();
  const ask = () => confirmation.ask({ title: 'Delete coupon SAVE10?', message: 'Deleting can\'t be undone.',
    confirmLabel: 'Delete coupon', danger: true, action });
  const body = <><button type="button" onClick={ask}>Delete</button>{confirmation.dialog}</>;
  return inDrawer ? <Drawer open title="Coupon" onClose={onDrawerClose}>{body}</Drawer> : body;
}

describe('useConfirmAction', () => {
  it('لا تنفيذ قبل التأكيد، والإلغاء لا ينفّذ', async () => {
    const action = vi.fn();
    const user = userEvent.setup();
    render(<Harness action={action} />);

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    expect(screen.getByRole('alertdialog', { name: 'Delete coupon SAVE10?' })).toBeInTheDocument();
    await user.click(screen.getByText('common.cancel'));

    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(action).not.toHaveBeenCalled();
  });

  it('التأكيد ينفّذ مرّة واحدة ويُغلق بالنجاح', async () => {
    const action = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<Harness action={action} />);

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await user.click(screen.getByRole('button', { name: 'Delete coupon' }));

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(action).toHaveBeenCalledTimes(1);
  });

  it('رفض الخادم يُبقي الحوار مفتوحاً برسالته، وفتحٌ تالٍ يبدأ بلا خطأ', async () => {
    const action = vi.fn().mockRejectedValue(new Error('This coupon has been used on orders.'));
    const user = userEvent.setup();
    render(<Harness action={action} />);

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await user.click(screen.getByRole('button', { name: 'Delete coupon' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('This coupon has been used on orders.');

    await user.click(screen.getByText('common.cancel'));
    await user.click(screen.getByRole('button', { name: 'Delete' }));
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('فوق درج: Escape يغلق الحوار وحده ويُبقي الدرج', async () => {
    const onDrawerClose = vi.fn();
    const user = userEvent.setup();
    render(<Harness action={vi.fn()} inDrawer onDrawerClose={onDrawerClose} />);

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await user.keyboard('{Escape}');

    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(onDrawerClose).not.toHaveBeenCalled();

    await user.keyboard('{Escape}');
    expect(onDrawerClose).toHaveBeenCalledTimes(1);
  });
});
