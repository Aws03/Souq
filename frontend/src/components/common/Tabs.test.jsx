// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useId, useState } from 'react';

// ============================================================================
// ما يجعل لسانين تبويباً حقيقياً هو ما لا يُرى: الأدوار والروابط و**tabindex المتجوّل**. زرّان مُنسَّقان
// يبدوان تبويباً ولا يُقرأان تبويباً — فهذه الاختبارات على العقد الذي يقرؤه قارئ الشاشة ولوحة المفاتيح،
// لا على المظهر.
// ============================================================================
const { default: Tabs, TabPanel } = await import('./Tabs');

function Harness({ onChange = () => {} }) {
  const base = useId();
  const [active, setActive] = useState('a');
  const tabs = [
    { id: 'a', label: 'الأول' },
    { id: 'b', label: 'الثاني', badge: 3 },
    { id: 'c', label: 'الثالث' },
  ];

  return (
    <>
      <Tabs tabs={tabs} active={active} base={base} label="أقسام"
        onChange={(id) => { setActive(id); onChange(id); }} />
      <TabPanel id={active} base={base}>
        <button type="button">داخل اللوح</button>
      </TabPanel>
    </>
  );
}

const tabsOf = () => screen.getAllByRole('tab');

describe('Tabs', () => {
  it('اللسان المختار وحده في تسلسل الجدولة', () => {
    render(<Harness />);
    const [first, second, third] = tabsOf();

    expect(first).toHaveAttribute('aria-selected', 'true');
    expect(first).toHaveAttribute('tabindex', '0');
    // غير المختار خارج التسلسل: بلا هذا يمرّ Tab على كل لسانٍ ثمّ لا يصل إلى اللوح بوضوح.
    expect(second).toHaveAttribute('tabindex', '-1');
    expect(third).toHaveAttribute('tabindex', '-1');
  });

  it('اللسان يشير إلى لوحه واللوح يشير إليه', () => {
    render(<Harness />);
    const panel = screen.getByRole('tabpanel');
    const [first] = tabsOf();

    expect(first).toHaveAttribute('aria-controls', panel.id);
    expect(panel).toHaveAttribute('aria-labelledby', first.id);
  });

  // ============================================================================
  // اللوح **ليس** قابلاً للتركيز: نمط ARIA يجعله كذلك فقط إن خلا من عنصرٍ تفاعلي، ولوحُنا فيه أزرار.
  // محطّةٌ زائدة في تسلسل الجدولة لا تفعل شيئاً هي تدهورٌ لمن يتنقّل بلوحة المفاتيح، لا تحسين.
  // ============================================================================
  it('اللوح ليس محطّة في تسلسل الجدولة', () => {
    render(<Harness />);
    expect(screen.getByRole('tabpanel')).not.toHaveAttribute('tabindex');
  });

  it('النقر ينقل الاختيار', async () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);

    await userEvent.click(tabsOf()[1]);

    expect(onChange).toHaveBeenCalledWith('b');
    expect(tabsOf()[1]).toHaveAttribute('aria-selected', 'true');
    expect(tabsOf()[0]).toHaveAttribute('aria-selected', 'false');
  });

  // ============================================================================
  // الأسهم تتبع اتجاه القراءة. الحاوية في jsdom بلا `dir` فتُحسب LTR، والسهم الأيمن يمضي إلى الأمام.
  // (الحالة المقلوبة في الاختبار التالي: `dir="rtl"` على الحاوية نفسها.)
  // ============================================================================
  it('الأسهم تنقل بين الألسنة وتُغلق الدورة', async () => {
    render(<Harness />);
    tabsOf()[0].focus();

    await userEvent.keyboard('{ArrowRight}');
    expect(tabsOf()[1]).toHaveAttribute('aria-selected', 'true');
    expect(tabsOf()[1]).toHaveFocus();

    await userEvent.keyboard('{ArrowRight}{ArrowRight}');
    // ثالثٌ ثم دورةٌ إلى الأول: طريقٌ مسدود عند آخر لسانٍ يجعل التنقّل بالأسهم يبدو معطوباً.
    expect(tabsOf()[0]).toHaveAttribute('aria-selected', 'true');

    await userEvent.keyboard('{ArrowLeft}');
    expect(tabsOf()[2]).toHaveAttribute('aria-selected', 'true');

    await userEvent.keyboard('{Home}');
    expect(tabsOf()[0]).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{End}');
    expect(tabsOf()[2]).toHaveAttribute('aria-selected', 'true');
  });

  // ============================================================================
  // في RTL يمضي **السهم الأيسر** إلى الأمام: الأسهم تعني اتجاهاً على الشاشة، والشاشة مقلوبة. عكسُها
  // يجعل التنقّل في الواجهة العربية يسير عكس ما يراه التاجر — وهو عيبُ RTL الذي لا يظهر في أي لقطة.
  // ============================================================================
  it('في RTL السهم الأيسر يمضي إلى الأمام', async () => {
    render(<div dir="rtl"><Harness /></div>);
    tabsOf()[0].focus();

    await userEvent.keyboard('{ArrowLeft}');
    expect(tabsOf()[1]).toHaveAttribute('aria-selected', 'true');

    await userEvent.keyboard('{ArrowRight}');
    expect(tabsOf()[0]).toHaveAttribute('aria-selected', 'true');
  });

  it('العدّاد يُعرض حين يُمرَّر ويُحجب حين لا', () => {
    render(<Harness />);
    expect(tabsOf()[1]).toHaveTextContent('3');
    expect(tabsOf()[0].querySelector('[data-badge]')).toBeNull();
  });
});
