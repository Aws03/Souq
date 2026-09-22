// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// دفترُ فواتير التجّار (C5، ADR-0056).
//
// ما يُختبر هنا ليس أنّ الجدول يُرسَم، بل ثلاثةُ أشياء يقرؤها مشغّلٌ ويتصرّف بناءً عليها:
//   • أنّ **تعذُّر الإصدار يُقال بسببه** قبل أن يُضغط زرٌّ في شاشةٍ أخرى،
//   • أنّ المتأخّرة تُقرأ «متأخّرة» لا «صادرة»،
//   • وأنّ كلَّ مبلغٍ يُعرض **بعملة صفّه** — فمضيفُ المنصّة بلا عملةِ متجر، والجمعُ عبر العملات
//     ممنوعٌ في هذا المنتج أصلاً.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));

vi.mock('../../i18n', () => ({ formatDate: (v) => `on ${v}`, formatDateTime: (v) => `at ${v}` }));

// عملةُ الصفّ تصل إلى المُنسّق: هذا ما يُثبت أنّها لم تُؤخذ من متجرٍ لا وجود له على هذا المضيف.
vi.mock('../../components/product/ProductBadges', () => ({
  formatPrice: (amount, currency) => `${amount} ${currency}`,
}));

const client = vi.hoisted(() => ({
  getPlatformInvoices: vi.fn(),
  getPlatformBillingSettings: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const Invoices = (await import('./Invoices')).default;

const invoice = (patch) => ({
  id: 1, tenantId: 7, tenantName: 'متجر أ', number: 'INV000001', status: 'Issued', currency: 'JOD',
  periodStartUtc: '2026-09-01T00:00:00Z', periodEndUtc: '2026-10-01T00:00:00Z',
  issuedAtUtc: '2026-09-01T00:00:00Z', dueAtUtc: '2026-10-01T00:00:00Z',
  subtotal: 100, taxAmount: 0, total: 100, amountPaid: 0, credited: 0, outstanding: 100,
  isOverdue: false, daysOverdue: 0, ...patch,
});

const page = (items) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });

const ready = {
  currency: 'JOD', issuerName: 'سوق', canIssue: true, blockingReason: null, taxReason: 'NoProfileSelected',
};

const renderWithRouter = async () => {
  const { MemoryRouter } = await import('react-router-dom');
  return render(withQueryClient(<MemoryRouter><Invoices /></MemoryRouter>));
};

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  client.getPlatformBillingSettings.mockResolvedValue(ready);
  client.getPlatformInvoices.mockResolvedValue(page([invoice()]));
});

describe('القائمة', () => {
  it('المبالغ بعملة صفّها لا بعملة مفترضة', async () => {
    // الإجماليُّ والمتبقّي مختلفان عمداً: لو تساويا لما ميّز التوكيدُ عموداً عن آخر.
    client.getPlatformInvoices.mockResolvedValue(page([invoice({ amountPaid: 40, outstanding: 60 })]));
    await renderWithRouter();
    const row = (await screen.findByText('INV000001')).closest('tr');

    // `100 JOD` هنا تعني أنّ `formatPrice` تلقّت عملةَ الصفّ — ولو أُسقطت لصارت `100 undefined`.
    expect(within(row).getByText('100 JOD')).toBeInTheDocument();
    expect(within(row).getByText('60 JOD')).toBeInTheDocument();
  });

  it('الصادرةُ المتأخّرة تُقرأ متأخّرة، ومدّةُ تأخّرها بجانبها', async () => {
    client.getPlatformInvoices.mockResolvedValue(page([
      invoice({ id: 2, number: 'INV000002', isOverdue: true, daysOverdue: 12 }),
    ]));
    await renderWithRouter();
    await screen.findByText('INV000002');

    expect(screen.getByText('platform.invoices.status.Overdue')).toBeInTheDocument();
    expect(screen.getByText('platform.invoices.overdueBy:{"count":12}')).toBeInTheDocument();
  });

  it('المسوّدةُ بلا رقم تُقرأ باسمها لا بفراغ', async () => {
    client.getPlatformInvoices.mockResolvedValue(page([
      invoice({ id: 3, number: null, status: 'Draft', issuedAtUtc: null, dueAtUtc: null }),
    ]));
    await renderWithRouter();

    const row = (await screen.findByText('platform.invoices.unnumbered')).closest('tr');

    // داخل الصفّ لا في الشاشة كلّها: «مسوّدة» اسمٌ في مرشّح الحالة أيضاً، فالتوكيد الواسع
    // كان سيمرّ على خيارٍ في قائمةٍ منسدلة ويظنّ أنّه فحص شارةَ صفّ.
    expect(within(row).getByText('platform.invoices.status.Draft')).toBeInTheDocument();
  });
});

describe('جاهزيةُ الإصدار', () => {
  it('الإعدادُ ناقصٌ ⇒ السببُ مكتوبٌ في الشاشة مع طريقٍ إلى إصلاحه', async () => {
    // هذا هو الشكلُ الذي يدخل به قرارُ المالك `C-15` إلى الواجهة: لا زرَّ معطَّلاً بلا تفسير.
    client.getPlatformBillingSettings.mockResolvedValue({
      ...ready, canIssue: false, blockingReason: 'BillingCurrencyNotSet',
    });
    await renderWithRouter();

    expect(await screen.findByText('platform.billing.blocked.BillingCurrencyNotSet')).toBeInTheDocument();
    expect(screen.getByText('platform.billing.configure')).toBeInTheDocument();
  });

  it('الإعدادُ جاهزٌ ⇒ لا تحذير يشغل الشاشة', async () => {
    await renderWithRouter();
    await screen.findByText('INV000001');

    expect(screen.queryByText(/platform\.billing\.blocked/)).toBeNull();
  });
});

describe('المرشّحات', () => {
  it('تصفيةُ المتأخّرة تُرسَل إلى الخادم ولا تُحسب في المتصفّح', async () => {
    await renderWithRouter();
    await screen.findByText('INV000001');
    const user = userEvent.setup();

    await user.click(screen.getByLabelText('platform.invoices.overdueOnly'));

    await waitFor(() => expect(client.getPlatformInvoices).toHaveBeenLastCalledWith(
      expect.objectContaining({ overdueOnly: true, page: 1 }),
    ));
  });

  it('اختيارُ حالةٍ يُعيد إلى الصفحة الأولى', async () => {
    await renderWithRouter();
    await screen.findByText('INV000001');
    const user = userEvent.setup();

    await user.selectOptions(screen.getByLabelText('platform.invoices.statusLabel'), 'Settled');

    await waitFor(() => expect(client.getPlatformInvoices).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'Settled', page: 1 }),
    ));
  });
});
