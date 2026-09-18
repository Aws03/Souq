// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ============================================================================
// درج العنوان (TD-32 — آخر النماذج التي بقيت بلا اختبار شاشة).
//
// قواعد التحويل نفسها (القصّ، الدولة بحرفين كبيرين، الاختياري الفارغ ⇒ null) مُختبَرة نقيّةً في
// `addressForm.test.js`، وصيغة الهاتف ورمز الدولة يحرسهما الكيان على الخادم. فما يُختبر هنا هو ما
// لا يُقاس إلا بالشاشة:
//   • نموذج ناقص **لا يُرسَل**، والخطأ يُعلَّق على الحقل الناقص وحده لا على كل حقل فارغ،
//   • ما يصل إلى `onSave` هو جسم الخادم لا حالة النموذج (فرقٌ يخفيه اختبار المنطق وحده)،
//   • خيارا "الافتراضي" يظهران عند الإضافة فقط — بعدها يُضبطان من البطاقة، وعرضهما هنا يكذب،
//   • ورفض الخادم يبقى **داخل الدرج** ويُعيد الزرّ قابلاً للضغط: نموذجٌ يُقفل على خطأ يُفقد ما كُتب.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key) => key,
    i18n: { dir: () => 'ltr', language: 'en', exists: () => true },
  }),
}));

const AddressFormDrawer = (await import('./AddressFormDrawer')).default;

const SAVED = {
  id: 7, label: 'Home', recipientName: 'Aws', phone: '0790000000', country: 'JO',
  city: 'Amman', region: 'Amman', line1: 'Street 1', line2: '', postalCode: '11181',
};

const field = (key) => screen.getByLabelText(`account.field.${key}`);
const save = (user) => user.click(screen.getByRole('button', { name: 'common.save' }));

let onSave;
let onClose;
beforeEach(() => {
  onSave = vi.fn().mockResolvedValue(undefined);
  onClose = vi.fn();
});

describe('AddressFormDrawer', () => {
  it('لا يُرسل نموذجاً ناقصاً، ويُعلّم الحقول المطلوبة وحدها', async () => {
    const user = userEvent.setup();
    render(<AddressFormDrawer onSave={onSave} onClose={onClose} />);

    await save(user);

    expect(onSave).not.toHaveBeenCalled();
    // خمسة مطلوبة: المستلم، الهاتف، الدولة، المدينة، السطر الأول.
    expect(screen.getAllByText('account.fieldRequired')).toHaveLength(5);
    // والاختياري الفارغ لا يُعلَّم: `region` و`line2` و`postalCode` و`label` فارغة كلها.
    expect(field('region')).toHaveValue('');
  });

  it('يُرسل جسم الخادم لا حالة النموذج: مقصوصاً، والدولة بحرفين كبيرين، والاختياري الفارغ null', async () => {
    const user = userEvent.setup();
    render(<AddressFormDrawer onSave={onSave} onClose={onClose} />);

    await user.type(field('recipientName'), '  Aws  ');
    await user.type(field('phone'), ' 0790000000 ');
    await user.type(field('country'), 'jo');
    await user.type(field('city'), ' Amman ');
    await user.type(field('line1'), ' Street 1 ');
    await save(user);

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0][0]).toEqual({
      recipientName: 'Aws', phone: '0790000000', country: 'JO', city: 'Amman', line1: 'Street 1',
      region: null, line2: null, postalCode: null, label: null,
    });
  });

  it('خيارا الافتراضي عند الإضافة فقط، ويصلان كما اختارهما العميل', async () => {
    const user = userEvent.setup();
    const { unmount } = render(<AddressFormDrawer onSave={onSave} onClose={onClose} />);

    await user.click(screen.getByLabelText('account.useAsDefaultShipping'));
    await user.type(field('recipientName'), 'Aws');
    await user.type(field('phone'), '0790000000');
    await user.type(field('country'), 'JO');
    await user.type(field('city'), 'Amman');
    await user.type(field('line1'), 'Street 1');
    await save(user);

    await waitFor(() => expect(onSave).toHaveBeenCalled());
    expect(onSave.mock.calls[0][1]).toEqual({ shipping: true, billing: false });

    unmount();
    render(<AddressFormDrawer address={SAVED} onSave={onSave} onClose={onClose} />);
    expect(screen.queryByLabelText('account.useAsDefaultShipping')).not.toBeInTheDocument();
  });

  it('التعديل يبدأ بقيم العنوان المحفوظ', () => {
    render(<AddressFormDrawer address={SAVED} onSave={onSave} onClose={onClose} />);

    expect(field('recipientName')).toHaveValue('Aws');
    expect(field('city')).toHaveValue('Amman');
    expect(field('postalCode')).toHaveValue('11181');
  });

  it('رفض الخادم يبقى داخل الدرج ولا يُفقد ما كُتب', async () => {
    const user = userEvent.setup();
    onSave.mockRejectedValue(new Error('InvalidCustomerData'));
    render(<AddressFormDrawer onSave={onSave} onClose={onClose} />);

    await user.type(field('recipientName'), 'Aws');
    await user.type(field('phone'), 'not-a-phone');
    await user.type(field('country'), 'JO');
    await user.type(field('city'), 'Amman');
    await user.type(field('line1'), 'Street 1');
    await save(user);

    expect(await screen.findByText('InvalidCustomerData')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(field('phone')).toHaveValue('not-a-phone');
    // والزرّ عاد قابلاً للضغط: نموذجٌ يبقى مشغولاً بعد الرفض لا يمكن تصحيحه.
    expect(screen.getByRole('button', { name: 'common.save' })).toBeEnabled();
  });
});
