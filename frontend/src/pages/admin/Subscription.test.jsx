// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// اشتراكُ المتجر كما يراه تاجرُه (C5، ADR-0056).
//
// **وأهمُّ ما يُختبر هنا سلبيّ**: لا زرَّ دفعٍ في هذه الشاشة. التحصيلُ حوالةٌ بنكية بقرار المالك
// `C-15`، فما يحتاجه التاجر أن يقرأ المبلغ وتعليماتِ التحويل — وزرُّ «ادفع الآن» يَعِد بما لا
// يوجد. وإضافةُ واحدٍ يوماً بلا مزوّدٍ خلفه يجب أن تُفشل اختباراً لا أن تمرّ.
//
// وما بعده: أنّ «بلا سعر» تُقرأ نصّاً لا فراغاً، وأنّ التأخّر يُقرأ بمدّته.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));

vi.mock('../../i18n', () => ({ formatDate: (v) => `on ${v}`, formatDateTime: (v) => `at ${v}` }));
vi.mock('../../components/product/ProductBadges', () => ({
  formatPrice: (amount, currency) => `${amount} ${currency}`,
}));

const client = vi.hoisted(() => ({
  getMySubscription: vi.fn(),
  getMyInvoices: vi.fn(),
  getMyInvoice: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const Subscription = (await import('./Subscription')).default;

const summary = (patch) => ({
  planCode: 'growth', planName: 'خطة النموّ', planVersion: 1, subscriptionStatus: 'Active',
  subscribedAtUtc: '2026-01-01T00:00:00Z', priceAmount: 25, priceCurrency: 'JOD', billingIntervalMonths: 1,
  outstandingTotal: 100, outstandingCurrency: 'JOD', openInvoiceCount: 1, overdueInvoiceCount: 0,
  paymentInstructions: 'حوّل إلى الحساب الاختباري', ...patch,
});

const invoice = (patch) => ({
  id: 1, number: 'INV000001', status: 'Issued', currency: 'JOD',
  periodStartUtc: '2026-09-01T00:00:00Z', periodEndUtc: '2026-10-01T00:00:00Z',
  issuedAtUtc: '2026-09-01T00:00:00Z', dueAtUtc: '2026-10-01T00:00:00Z',
  subtotal: 100, taxAmount: 0, total: 100, amountPaid: 0, credited: 0, outstanding: 100,
  isOverdue: false, daysOverdue: 0, lines: [], payments: [], creditNotes: [], ...patch,
});

const page = (items) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  client.getMySubscription.mockResolvedValue(summary());
  client.getMyInvoices.mockResolvedValue(page([invoice()]));
});

describe('الملخّص', () => {
  it('لا زرَّ دفعٍ في الشاشة: التحصيل حوالةٌ، وتعليماتُها هي الجواب', async () => {
    render(withQueryClient(<Subscription />));
    await screen.findByText('INV000001');

    expect(screen.getByText('admin.subscription.howToPay')).toBeInTheDocument();
    expect(screen.getByText('حوّل إلى الحساب الاختباري')).toBeInTheDocument();

    // لا زرٌّ يَعِد بما لا يوجد. القائمةُ البيضاء صريحة: ما في الشاشة من أزرار هو قائمةُ الصفّ.
    const buttons = screen.getAllByRole('button').map((b) => b.textContent);
    expect(buttons.some((label) => /pay|ادفع|سدّد/i.test(label ?? ''))).toBe(false);
  });

  it('خطةٌ بلا سعر تُقرأ نصّاً لا فراغاً', async () => {
    // «بلا سعر» ليست صفراً: الأولى تعني أنّ أحداً لم يقرّر بعد، والثانية قرارٌ صريح.
    client.getMySubscription.mockResolvedValue(summary({ priceAmount: null, priceCurrency: null }));
    render(withQueryClient(<Subscription />));

    expect(await screen.findByText('admin.subscription.noPrice')).toBeInTheDocument();
  });

  it('السعرُ يُقرأ بدورته لا بمبلغه وحده', async () => {
    client.getMySubscription.mockResolvedValue(summary({ billingIntervalMonths: 3 }));
    render(withQueryClient(<Subscription />));

    expect(await screen.findByText('admin.subscription.pricePerInterval:{"price":"25 JOD","count":3}'))
      .toBeInTheDocument();
  });

  it('تعليماتُ الدفع تختفي حين لا يبقى شيء', async () => {
    // إظهارُ «كيف تدفع» لتاجرٍ لا يدين بشيء يدعوه إلى تحويلٍ لا سبب له.
    client.getMySubscription.mockResolvedValue(summary({ outstandingTotal: 0, openInvoiceCount: 0 }));
    client.getMyInvoices.mockResolvedValue(page([invoice({ status: 'Settled', amountPaid: 100, outstanding: 0 })]));
    render(withQueryClient(<Subscription />));
    await screen.findByText('INV000001');

    expect(screen.queryByText('admin.subscription.howToPay')).toBeNull();
  });
});

describe('قائمةُ الفواتير', () => {
  it('المتأخّرةُ تُقرأ بمدّة تأخّرها', async () => {
    client.getMyInvoices.mockResolvedValue(page([invoice({ isOverdue: true, daysOverdue: 5 })]));
    render(withQueryClient(<Subscription />));
    const row = (await screen.findByText('INV000001')).closest('tr');

    expect(within(row).getByText('admin.subscription.status.Overdue')).toBeInTheDocument();
    expect(within(row).getByText('admin.subscription.overdueBy:{"count":5}')).toBeInTheDocument();
  });

  it('الدرجُ يفتح المستند بأسطره ومبالغه', async () => {
    client.getMyInvoice.mockResolvedValue(invoice({
      lines: [{ id: 9, description: 'اشتراك أيلول', quantity: 1, unitAmount: 100, lineTotal: 100, taxCategory: 'standard' }],
    }));
    render(withQueryClient(<Subscription />));
    const row = (await screen.findByText('INV000001')).closest('tr');
    const user = userEvent.setup();

    await user.click(within(row).getByRole('button'));
    await user.click(await screen.findByRole('menuitem', { name: 'admin.subscription.view' }));

    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByText('اشتراك أيلول')).toBeInTheDocument();
    expect(client.getMyInvoice).toHaveBeenCalledWith(1);
  });
});
