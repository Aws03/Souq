// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// حوار التأكيد: اسم ووصف مقروءان، إلغاء بـ Escape، وتأكيد مكتوب لا يُتجاوَز بنقرة.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key, values) => (values ? `${key}:${JSON.stringify(values)}` : key) }),
}));
const ConfirmDialog = (await import('./ConfirmDialog')).default;

const renderDialog = (props = {}) => {
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  render(<ConfirmDialog open title="Archive Acme?" message="This cannot be undone." confirmLabel="Archive"
    onConfirm={onConfirm} onCancel={onCancel} {...props} />);
  return { onConfirm, onCancel };
};

describe('ConfirmDialog', () => {
  it('حوار تنبيه باسمه ووصفه', () => {
    renderDialog();
    const dialog = screen.getByRole('alertdialog', { name: 'Archive Acme?' });
    expect(dialog).toHaveAccessibleDescription('This cannot be undone.');
  });

  it('Escape يُلغي', async () => {
    const { onCancel } = renderDialog();
    await userEvent.keyboard('{Escape}');
    expect(onCancel).toHaveBeenCalled();
  });

  it('التأكيد المكتوب: الزرّ معطّل حتى يُكتب النصّ حرفياً', async () => {
    const { onConfirm } = renderDialog({ requireText: 'acme', danger: true });
    const confirm = screen.getByRole('button', { name: 'Archive' });
    expect(confirm).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/common\.typeToConfirm/), 'acm');
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/common\.typeToConfirm/), 'e{Enter}');
    expect(confirm).toBeEnabled();
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('أثناء التنفيذ لا إلغاء، وخطأ الخادم يُقرأ داخل الحوار', async () => {
    const { onCancel } = renderDialog({ busy: true, error: 'Store already archived' });
    await userEvent.keyboard('{Escape}');
    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('Store already archived');
  });

  it('مغلق ⇒ لا شيء', () => {
    render(<ConfirmDialog open={false} title="x" message="y" confirmLabel="z" onConfirm={() => {}} onCancel={() => {}} />);
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });
});
