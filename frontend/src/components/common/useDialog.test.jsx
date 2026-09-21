// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useDialog } from './useDialog';

// ============================================================================
// TD-48 — حبس التركيز داخل الحوار.
//
// **ما يمنعه هذا الملف ليس إزعاجاً بل كذبة.** الدرج يعلن `aria-modal="true"`، أي: ما خلفي غير
// متاح. وبلا الحبس كان Tab يخرج إلى الصفحة خلفه، فيصل مستعملُ لوحة المفاتيح وقارئُ الشاشة إلى
// أزرار تحجبها الطبقة فوقها — يضغط ما لا يرى، في صفحةٍ أعلنت للتوّ أنها غير متاحة.
//
// ولا يحبسه إلى الأبد: Escape يغلق، وهو الفرق بين حبسٍ وفخّ.
// ============================================================================

function Fixture({ open = true, onClose = () => {}, empty = false }) {
  const panelRef = useDialog(open, onClose);
  return (
    <>
      <button type="button">behind</button>
      {open && (
        <div ref={panelRef} role="dialog" aria-modal="true" aria-label="drawer" tabIndex={-1}>
          {!empty && (
            <>
              <button type="button">first</button>
              <input aria-label="middle" />
              <button type="button">last</button>
            </>
          )}
        </div>
      )}
    </>
  );
}

describe('useDialog focus trap', () => {
  it('ينقل التركيز إلى اللوحة عند الفتح', () => {
    render(<Fixture />);
    expect(document.activeElement).toBe(screen.getByRole('dialog'));
  });

  it('Tab من آخر عنصر يعود إلى أوّله لا إلى الصفحة خلفه', async () => {
    const user = userEvent.setup();
    render(<Fixture />);

    screen.getByRole('button', { name: 'last' }).focus();
    await user.tab();

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));
    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: 'behind' }));
  });

  it('Shift+Tab من أوّل عنصر يذهب إلى آخره', async () => {
    const user = userEvent.setup();
    render(<Fixture />);

    screen.getByRole('button', { name: 'first' }).focus();
    await user.tab({ shift: true });

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'last' }));
  });

  it('Tab من اللوحة نفسها يدخل أوّل عنصر، وShift+Tab يدخل آخره', async () => {
    const user = userEvent.setup();
    const { unmount } = render(<Fixture />);

    await user.tab();
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));
    unmount();

    render(<Fixture />);
    await user.tab({ shift: true });
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'last' }));
  });

  it('Tab في الوسط يُترك للمتصفّح — الترتيب الطبيعي أدقّ من أي ترتيب نعيد حسابه', async () => {
    const user = userEvent.setup();
    render(<Fixture />);

    screen.getByRole('button', { name: 'first' }).focus();
    await user.tab();

    expect(document.activeElement).toBe(screen.getByLabelText('middle'));
  });

  it('درجٌ بلا عنصر قابل للتركيز يُبقي التركيز على لوحته', async () => {
    const user = userEvent.setup();
    render(<Fixture empty />);

    await user.tab();

    expect(document.activeElement).toBe(screen.getByRole('dialog'));
  });

  it('Escape يبقى يغلق — الحبس ليس فخّاً', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(<Fixture onClose={onClose} />);

    await user.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledOnce();
  });

  it('لا حبس وهو مغلق: Tab يتحرّك في الصفحة كالمعتاد', async () => {
    const user = userEvent.setup();
    render(<Fixture open={false} />);

    await user.tab();

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'behind' }));
  });
});
