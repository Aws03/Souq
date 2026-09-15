// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// الدرج نافذة حوارية (role=dialog، aria-modal) — وكان يفتقر إلى ما يجعل ذلك صحيحاً: لا Escape،
// ولا نقل تركيز، والصفحة خلفه تتحرّك. من يستعمل لوحة المفاتيح أو قارئ شاشة كان يدخل ولا يخرج.
// ============================================================================
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key) => key }) }));

const Drawer = (await import('./Drawer')).default;

const open = (props = {}) => render(
  <>
    <button type="button">opener</button>
    <Drawer open onClose={props.onClose ?? vi.fn()} title="Cart" {...props}>
      <button type="button">inside</button>
    </Drawer>
  </>
);

describe('Drawer', () => {
  it('Escape يغلقه', async () => {
    const onClose = vi.fn();
    open({ onClose });

    await userEvent.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('لا يغلق بـEscape أثناء الحفظ', async () => {
    // نفس شرط زرّ الإغلاق المعطَّل: إغلاق في منتصف حفظ يترك المستخدم لا يدري ما جرى.
    const onClose = vi.fn();
    open({ onClose, busy: true });

    await userEvent.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('الخلفية زرّ له اسم، لا <div> صامت', async () => {
    const onClose = vi.fn();
    open({ onClose });

    const backdrops = screen.getAllByRole('button', { name: 'common.close' });
    expect(backdrops.length).toBeGreaterThan(0);
    await userEvent.click(backdrops[0]);
    expect(onClose).toHaveBeenCalled();
  });

  it('التركيز ينتقل إلى الدرج عند فتحه', () => {
    open();
    expect(document.activeElement).toBe(screen.getByRole('dialog'));
  });

  it('التركيز يعود إلى ما فتحه عند إغلاقه', () => {
    const opener = document.createElement('button');
    document.body.append(opener);
    opener.focus();

    const { rerender } = render(
      <Drawer open onClose={vi.fn()} title="Cart"><p>body</p></Drawer>
    );
    expect(document.activeElement).not.toBe(opener);

    rerender(<Drawer open={false} onClose={vi.fn()} title="Cart"><p>body</p></Drawer>);
    expect(document.activeElement).toBe(opener);
    opener.remove();
  });

  it('يوقف تمرير الصفحة خلفه ويعيده', () => {
    const { rerender } = render(<Drawer open onClose={vi.fn()} title="Cart"><p>body</p></Drawer>);
    expect(document.body.style.overflow).toBe('hidden');

    rerender(<Drawer open={false} onClose={vi.fn()} title="Cart"><p>body</p></Drawer>);
    expect(document.body.style.overflow).not.toBe('hidden');
  });
});
