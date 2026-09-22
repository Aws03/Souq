// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { withQueryClient } from '../../test/queryWrapper';

// ============================================================================
// ضريبةُ المتجر كما يضبطها تاجره ([ADR-0055](0055)).
//
// **وأهمُّ ما يُختبر هنا سلبيّ، كما في شاشة الاشتراك**: لا حقلَ نسبةٍ في هذه الشاشة. النسبةُ
// قاعدةُ اختصاصٍ تحفظها المنصّة ويؤكّدها مهنيّ، ولو استطاع تاجرٌ إدخالَها لَما كان للتحقّق معنى.
// فإضافةُ حقلٍ كهذا يوماً يجب أن تُفشل اختباراً لا أن تمرّ في مراجعة.
//
// وبعده: أنّ صفر الضريبة **يُقال بسببه** في كل حالةٍ من حالاته الأربع.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key, values) => (values && Object.keys(values).length ? `${key}:${JSON.stringify(values)}` : key),
    i18n: { dir: () => 'rtl', language: 'ar' },
  }),
}));
vi.mock('../../i18n', () => ({ formatDate: (v) => `on ${v}`, formatDateTime: (v) => `at ${v}` }));

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../context/ToastContext', () => ({ useToast: () => toast }));

const client = vi.hoisted(() => ({
  getStoreTax: vi.fn(),
  updateStoreTax: vi.fn(),
  getStoreTaxProfiles: vi.fn(),
}));
vi.mock('../../api/client', () => ({ api: client }));

const StoreTax = (await import('./StoreTax')).default;

const settings = (patch) => ({
  taxProfileId: null, jurisdiction: null, profileName: null, collectionEnabled: false,
  registrationNumber: null, selectedAt: null, effectiveVersion: null,
  collecting: false, reason: 'NoProfileSelected', ...patch,
});

const verifiedVersion = {
  id: 1, version: 1, effectiveFrom: '2026-01-01T00:00:00Z', status: 'Published',
  priceMode: 'Exclusive', shippingTaxable: false,
  verificationState: 'Verified', verifiedBy: 'مكتب محاسبة', verifiedAt: '2026-02-01T00:00:00Z',
  verificationNote: null, registrationThreshold: null, notes: null,
  rates: [{ code: 'standard', name: 'Standard', basisPoints: 1600, category: 'standard' }],
};

beforeEach(() => {
  Object.values(client).forEach((fn) => fn.mockReset());
  toast.success.mockReset(); toast.error.mockReset();
  client.getStoreTax.mockResolvedValue(settings());
  client.getStoreTaxProfiles.mockResolvedValue([
    { id: 4, jurisdiction: 'TST', name: 'Test jurisdiction', versions: [], anyVersionAllowsCollection: true },
  ]);
});

describe('ما لا يوجد في هذه الشاشة', () => {
  it('لا حقلَ نسبةٍ ولا نقاطَ أساس: المتجر يختار ولا يُدخل قاعدة', async () => {
    client.getStoreTax.mockResolvedValue(settings({
      taxProfileId: 4, jurisdiction: 'TST', profileName: 'Test jurisdiction',
      collectionEnabled: true, collecting: true, reason: 'Collecting', effectiveVersion: verifiedVersion,
    }));
    render(withQueryClient(<StoreTax />));
    await screen.findByText('admin.tax.collecting');

    // النسبةُ تُعرَض للقراءة، ولا يوجد أيُّ حقلٍ رقميّ يُدخلها.
    expect(screen.getByText('16%')).toBeInTheDocument();
    expect(screen.queryByRole('spinbutton')).toBeNull();
  });
});

describe('سببُ عدم الجمع', () => {
  it.each([
    ['NoProfileSelected'],
    ['CollectionDisabled'],
    ['NoEffectiveVersion'],
    ['VersionNotVerified'],
  ])('%s يُقال بالاسم لا صفراً صامتاً', async (reason) => {
    client.getStoreTax.mockResolvedValue(settings({ reason }));
    render(withQueryClient(<StoreTax />));

    expect(await screen.findByText(`admin.tax.reason.${reason}`)).toBeInTheDocument();
    expect(screen.getByText('admin.tax.notCollecting')).toBeInTheDocument();
  });

  it('حالةُ التحقّق تظهر حيث تظهر النسبة', async () => {
    // قاعدةُ ADR-0055: لا يُقرأ رقمٌ بلا أن يظهر معه أنّ أحداً تحقّق منه.
    client.getStoreTax.mockResolvedValue(settings({
      taxProfileId: 4, jurisdiction: 'TST', profileName: 'Test jurisdiction', collectionEnabled: true,
      collecting: false, reason: 'VersionNotVerified',
      effectiveVersion: { ...verifiedVersion, verificationState: 'Unverified', verifiedBy: null, verifiedAt: null },
    }));
    render(withQueryClient(<StoreTax />));

    expect(await screen.findByText('admin.tax.verificationState.Unverified')).toBeInTheDocument();
    expect(screen.getByText('16%')).toBeInTheDocument();
  });
});

describe('الضبط', () => {
  it('لا يُفعَّل الجمع بلا اختصاصٍ مختار', async () => {
    render(withQueryClient(<StoreTax />));
    const collect = await screen.findByLabelText(/admin\.tax\.collect$/);

    // حالةٌ تقول «أجمع» بلا ملفّ تدّعي ما لا تفعل — يرفضها المجال، وتُمنع هنا قبل الرحلة.
    expect(collect).toBeDisabled();
  });

  it('الاختيارُ يُرسَل بمعرّفه، والنسبةُ لا تُرسَل إطلاقاً', async () => {
    client.updateStoreTax.mockResolvedValue(settings({ taxProfileId: 4 }));
    render(withQueryClient(<StoreTax />));
    const user = userEvent.setup();

    await user.selectOptions(await screen.findByLabelText('admin.tax.profile'), '4');
    await user.click(screen.getByLabelText(/admin\.tax\.collect$/));
    await user.click(screen.getByRole('button', { name: 'common.save' }));

    await waitFor(() => expect(client.updateStoreTax).toHaveBeenCalledWith({
      taxProfileId: 4, collectionEnabled: true, registrationNumber: null,
    }));
  });

  it('رفضُ الخادم يُقرأ في الشاشة لا يُبتلع', async () => {
    client.updateStoreTax.mockRejectedValue(Object.assign(new Error('الملفّ غير موجود'), { code: 'NotFound' }));
    render(withQueryClient(<StoreTax />));
    const user = userEvent.setup();

    await user.selectOptions(await screen.findByLabelText('admin.tax.profile'), '4');
    await user.click(screen.getByRole('button', { name: 'common.save' }));

    expect(await screen.findByText('الملفّ غير موجود')).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
  });
});
