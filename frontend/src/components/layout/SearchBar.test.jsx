// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { useState } from 'react';
import { withQueryClient } from '../../test/queryWrapper';
import { expectNoViolations } from '../../test/axe';

// ============================================================================
// صندوق البحث وقائمة اقتراحاته (M3، ADR-0042).
//
// معيار قبول M3 صريح: "الاقتراحات قابلة للتشغيل بلوحة المفاتيح ونظيفة في axe". هذا الملف هو ما يثبته — ولذلك
// يختبر **الخصائص التي يقرؤها قارئ الشاشة** (`aria-activedescendant`، `aria-expanded`، `role="option"`) لا
// الأصناف المرئية: قائمة تبدو صحيحة ولا يعرف قارئ الشاشة بوجودها هي العطل الذي يوجد هذا النمط لأجله.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, vars) => (vars ? `${key}:${vars.count}` : key),
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));

const client = vi.hoisted(() => ({ getSearchSuggestions: vi.fn() }));
vi.mock('../../api/client', () => ({ api: client }));

const SearchBar = (await import('./SearchBar')).default;

const product = (id, name) => ({ kind: 'product', id, slug: `p-${id}`, name, imageUrl: null });
const category = (id, name) => ({ kind: 'category', id, slug: `c-${id}`, name, imageUrl: null });

function Location() {
  const location = useLocation();
  return <div data-testid="url">{location.pathname}{location.search}</div>;
}

// صندوق متحكَّم فيه كما يستعمله Navbar فعلاً: القيمة من المستدعي لا من الصندوق.
function Harness({ onSubmit }) {
  const [value, setValue] = useState('');
  return withQueryClient(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="*" element={<><SearchBar value={value} onChange={setValue} onSubmit={onSubmit} /><Location /></>} />
      </Routes>
    </MemoryRouter>
  );
}

const box = () => screen.getByRole('combobox');
const options = () => screen.queryAllByRole('option');
const activeOption = () => {
  const id = box().getAttribute('aria-activedescendant');
  return id ? document.getElementById(id) : null;
};

async function type(text) {
  await userEvent.click(box());
  await userEvent.type(box(), text);
}

beforeEach(() => {
  client.getSearchSuggestions.mockReset();
  client.getSearchSuggestions.mockResolvedValue([
    product(1, 'مكنسة كهربائية'), product(2, 'مكنسة يدوية'), category(9, 'أجهزة منزلية'),
  ]);
});

describe('SearchBar — الحالة المُعلَنة', () => {
  it('مغلق ابتداءً ويعلن ذلك', () => {
    render(<Harness />);
    expect(box()).toHaveAttribute('aria-expanded', 'false');
    expect(box()).toHaveAttribute('aria-autocomplete', 'list');
    expect(box()).not.toHaveAttribute('aria-activedescendant');
    expect(options()).toHaveLength(0);
  });

  it('aria-controls يشير إلى قائمة موجودة فعلاً', () => {
    render(<Harness />);
    const listId = box().getAttribute('aria-controls');
    expect(listId).toBeTruthy();
    expect(document.getElementById(listId)).toHaveAttribute('role', 'listbox');
  });

  it('لا يطلب اقتراحات لحرف واحد', async () => {
    render(<Harness />);
    await type('م');
    await new Promise((r) => setTimeout(r, 350));
    expect(client.getSearchSuggestions).not.toHaveBeenCalled();
    expect(box()).toHaveAttribute('aria-expanded', 'false');
  });

  it('يفتح ويعلن العدد حين تصل الاقتراحات', async () => {
    render(<Harness />);
    await type('مكن');

    await waitFor(() => expect(box()).toHaveAttribute('aria-expanded', 'true'));
    expect(options()).toHaveLength(3);
    expect(await screen.findByText('nav.searchSuggestionsCount:3')).toBeInTheDocument();
  });

  it('لا يفتح حين لا اقتراحات', async () => {
    client.getSearchSuggestions.mockResolvedValue([]);
    render(<Harness />);
    await type('مكن');

    await waitFor(() => expect(client.getSearchSuggestions).toHaveBeenCalled());
    expect(box()).toHaveAttribute('aria-expanded', 'false');
  });
});

describe('SearchBar — لوحة المفاتيح', () => {
  it('الأسهم تنقل العنصر النشط وتلتفّ في الطرفين', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await userEvent.keyboard('{ArrowDown}');
    expect(activeOption()).toHaveTextContent('مكنسة كهربائية');
    expect(activeOption()).toHaveAttribute('aria-selected', 'true');

    await userEvent.keyboard('{ArrowDown}{ArrowDown}');
    expect(activeOption()).toHaveTextContent('أجهزة منزلية');

    // الالتفاف: من الأخير إلى الأول، ومن الأول إلى الأخير.
    await userEvent.keyboard('{ArrowDown}');
    expect(activeOption()).toHaveTextContent('مكنسة كهربائية');
    await userEvent.keyboard('{ArrowUp}');
    expect(activeOption()).toHaveTextContent('أجهزة منزلية');
  });

  it('Home و End يقفزان إلى الطرفين', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await userEvent.keyboard('{End}');
    expect(activeOption()).toHaveTextContent('أجهزة منزلية');
    await userEvent.keyboard('{Home}');
    expect(activeOption()).toHaveTextContent('مكنسة كهربائية');
  });

  it('عنصر واحد نشط فقط في كل لحظة', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));
    await userEvent.keyboard('{ArrowDown}{ArrowDown}');

    expect(options().filter((o) => o.getAttribute('aria-selected') === 'true')).toHaveLength(1);
  });

  it('Enter على عنصر نشط يفتحه ولا يُنفّذ البحث', async () => {
    const onSubmit = vi.fn();
    render(<Harness onSubmit={onSubmit} />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await userEvent.keyboard('{ArrowDown}{Enter}');

    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('/products/p-1'));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('Enter بلا عنصر نشط يُنفّذ البحث كما كان قبل الاقتراحات', async () => {
    const onSubmit = vi.fn();
    render(<Harness onSubmit={onSubmit} />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await userEvent.keyboard('{Enter}');

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('url')).toHaveTextContent('/');
  });

  it('Escape يغلق بلا تنقّل، والكتابة تُعيد الفتح', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await userEvent.keyboard('{Escape}');
    expect(box()).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByTestId('url')).toHaveTextContent('/');

    await userEvent.type(box(), 'س');
    await waitFor(() => expect(box()).toHaveAttribute('aria-expanded', 'true'));
  });

  it('Tab يغلق ولا يختار — الخروج بالمفتاح ليس تأكيداً', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));
    await userEvent.keyboard('{ArrowDown}');

    await userEvent.tab();

    expect(box()).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByTestId('url')).toHaveTextContent('/');
  });
});

describe('SearchBar — الاختيار بالفأرة', () => {
  it('النقر على منتج يفتح صفحته', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await userEvent.click(screen.getByText('مكنسة يدوية'));

    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('/products/p-2'));
  });

  it('النقر على فئة يفتح الكتالوج مصفّى بها لا صفحة منتج', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await userEvent.click(screen.getByText('أجهزة منزلية'));

    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('cats=9'));
  });

  it('النقر لا يُخرج التركيز من الحقل قبل أن تصل النقرة', async () => {
    render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    // لو خرج التركيز عند mousedown لأُغلقت القائمة ولم يصل onClick أبداً — وهو العطل المقصود بالحراسة.
    await userEvent.click(screen.getByText('مكنسة كهربائية'));
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('/products/p-1'));
  });
});

describe('SearchBar — إمكانية الوصول', () => {
  it('القائمة المفتوحة نظيفة في axe', async () => {
    const { container } = render(<Harness />);
    await type('مكن');
    await waitFor(() => expect(options()).toHaveLength(3));

    await expectNoViolations(container);
  });

  it('اختيار اقتراح يُبلِّغ المستدعي كي تُغلق ورقة الجوال', async () => {
    // بلا هذا يبقى الحوار فوق الصفحة الجديدة وتمريرها محجوز — يبدو المتجر معلّقاً.
    const onNavigate = vi.fn();
    render(
      withQueryClient(
        <MemoryRouter>
          <SearchBar value="مكن" onChange={() => {}} onNavigate={onNavigate} />
        </MemoryRouter>
      )
    );
    await userEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.queryAllByRole('option')).toHaveLength(3));

    await userEvent.click(screen.getByText('مكنسة كهربائية'));

    expect(onNavigate).toHaveBeenCalledTimes(1);
  });

  it('مثيلان في الصفحة لا يتشاركان معرّفات', async () => {
    // الصندوق يُركَّب مرّتين فعلاً (سطح المكتب وورقة الجوال) وكلاهما في الشجرة معاً.
    render(
      withQueryClient(
        <MemoryRouter>
          <SearchBar value="" onChange={() => {}} />
          <SearchBar value="" onChange={() => {}} />
        </MemoryRouter>
      )
    );

    const [first, second] = screen.getAllByRole('combobox');
    expect(first.getAttribute('aria-controls')).not.toBe(second.getAttribute('aria-controls'));
  });
});
