// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// إعدادُ فوترة المنصّة، ومعه **المفتاحُ الوحيد في المنتج الذي يُغلق متجرَ عميلٍ يدفع** (C6،
// ADR-0058).
//
// ما يُختبر هنا ليس أنّ النموذج يُرسَل، بل ثلاثةُ أشياء يقع على كلٍّ منها ضررٌ حقيقيّ لو انكسرت:
//   • أنّ المطالبة **معطّلةٌ ما لم تُقرأ مفعّلةً من الخادم** — الافتراضُ لا يُخترع في الواجهة،
//   • أنّها **لا تُفعَّل بمهلة سماحٍ صفر** (الدومين يرفض، والشاشة تمنع قبل أن يُرفَض)،
//   • وأنّ القيم تصل الخادم **أرقاماً** لا نصوصاً: حقلُ `number` في HTML يُعطي نصّاً دائماً،
//     و`"7"` في مُهلةٍ تُقارن عدديّاً عطبٌ لا يظهر إلّا حين يُعلَّق متجرٌ في اليوم الخطأ.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key) => key,
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));

const client = vi.hoisted(() => ({
  getPlatformBillingSettings: vi.fn(),
  getPlatformTaxProfiles: vi.fn(),
  updatePlatformBillingSettings: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const BillingSettings = (await import('./BillingSettings')).default;

const settings = (patch) => ({
  currency: 'JOD', issuerName: 'سوق', issuerAddress: null, issuerTaxNumber: null,
  invoiceNumberPrefix: 'INV', creditNoteNumberPrefix: 'CRN',
  paymentTermsDays: 30, gracePeriodDays: 7, paymentInstructions: null,
  taxProfileId: null, taxJurisdiction: null, taxProfileName: null, taxCollectionEnabled: false,
  canIssue: true, blockingReason: null, taxReason: 'NoProfileSelected',
  dunningEnabled: false, reminderIntervalDays: 7, maxRemindersBeforeSuspension: 3,
  ...patch,
});

const renderPage = async () => {
  const { MemoryRouter } = await import('react-router-dom');
  return render(withQueryClient(<MemoryRouter><BillingSettings /></MemoryRouter>));
};

const dunningToggle = () => screen.getByRole('checkbox', { name: /enableDunning/ });

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  client.getPlatformTaxProfiles.mockResolvedValue([]);
  client.updatePlatformBillingSettings.mockResolvedValue({});
});

describe('المطالبة الآلية في شاشة الإعداد', () => {
  it('تبدأ معطّلةً، ويظهر تحذيرُها حيث يُضغط المفتاح', async () => {
    client.getPlatformBillingSettings.mockResolvedValue(settings());
    await renderPage();

    expect(await screen.findByText('platform.billing.dunning')).toBeInTheDocument();
    expect(dunningToggle()).not.toBeChecked();

    // التحذيرُ لا يعيش في وثيقةٍ يبحث عنها من يبحث: هو في الشاشة نفسها.
    expect(screen.getByRole('note')).toHaveTextContent('platform.billing.dunningWarning');
  });

  it('تُقرأ مفعّلةً بقيمها حين يقولها الخادم', async () => {
    client.getPlatformBillingSettings.mockResolvedValue(
      settings({ dunningEnabled: true, reminderIntervalDays: 3, maxRemindersBeforeSuspension: 5 }));
    await renderPage();

    await waitFor(() => expect(dunningToggle()).toBeChecked());
    expect(screen.getByLabelText('platform.billing.reminderInterval')).toHaveValue(3);
    expect(screen.getByLabelText('platform.billing.maxReminders')).toHaveValue(5);
  });

  // مهلةُ سماحٍ صفر مع مطالبةٍ مفعّلة تعني التعليقَ في اليوم التالي للاستحقاق. الدومين يرمي،
  // والشاشةُ تمنع قبل أن يرمي — فالرسالةُ عند المفتاح أنفعُ من بلاغِ خطأٍ بعد الحفظ.
  it('لا تُفعَّل بمهلة سماحٍ صفر', async () => {
    client.getPlatformBillingSettings.mockResolvedValue(settings({ gracePeriodDays: 0 }));
    await renderPage();

    await waitFor(() => expect(dunningToggle()).toBeDisabled());
  });

  it('تُرسل أعدادها أرقاماً لا نصوصاً', async () => {
    const user = userEvent.setup();
    client.getPlatformBillingSettings.mockResolvedValue(settings());
    await renderPage();

    await user.click(await screen.findByRole('checkbox', { name: /enableDunning/ }));
    await user.clear(screen.getByLabelText('platform.billing.maxReminders'));
    await user.type(screen.getByLabelText('platform.billing.maxReminders'), '2');
    await user.click(screen.getByRole('button', { name: 'common.save' }));

    await waitFor(() => expect(client.updatePlatformBillingSettings).toHaveBeenCalled());
    expect(client.updatePlatformBillingSettings.mock.calls[0][0]).toMatchObject({
      dunningEnabled: true,
      reminderIntervalDays: 7,
      maxRemindersBeforeSuspension: 2,
    });
  });
});
