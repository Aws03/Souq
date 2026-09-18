// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expectNoViolations } from '../../test/axe';

// ============================================================================
// نموذج التقييم (TD-32).
//
// الأحقّية (اشترى، استلم، ولم يقيّم من قبل) يحكمها الخادم وحده، وهذا النموذج **لا يخمّنها** — فما
// يُختبر هنا هو ما لا يُقاس إلا بالشاشة:
//   • نجمةٌ بلا نصّ، أو نصٌّ بلا نجمة، لا يُرسَل — والرسالة تقول أيّهما ناقص،
//   • متجرٌ بالإشراف يعيد `Pending`: يُقال للعميل إنّ تقييمه ينتظر المراجعة، وإلّا بحث عنه في القائمة ولم يجده،
//   • رفض الخادم يُعرض **كما أعاده** ولا يُفرَّغ النموذج — وإلّا ضاع ما كتبه العميل على خطأ قد يكون مؤقّتاً،
//   • والنجاح يُفرِّغ النموذج فلا يُرسَل التقييم نفسه مرّتين.
// ============================================================================
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key) => key,
    i18n: { dir: () => 'ltr', language: 'en', exists: () => true },
  }),
}));

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../context/ToastContext', () => ({ useToast: () => toast }));

const apiMock = vi.hoisted(() => ({ createReview: vi.fn() }));
vi.mock('../../api/client', () => ({ api: apiMock }));

const ReviewForm = (await import('./ReviewForm')).default;

const submit = (user) => user.click(screen.getByRole('button', { name: 'reviews.submit' }));
const rate = (user, stars) => user.click(screen.getByLabelText(`product.starAria`, { exact: false, selector: `button:nth-of-type(${stars})` }));

beforeEach(() => {
  apiMock.createReview.mockReset().mockResolvedValue({ status: 'Published' });
  toast.success.mockReset(); toast.error.mockReset();
});

describe('ReviewForm', () => {
  it('لا مخالفة إتاحة بنيوية في النموذج', async () => {
    const { container } = render(<ReviewForm productId={1} />);
    await expectNoViolations(container);
  });

  it('بلا نجمة لا يُرسَل، والرسالة تقول إنّ التقييم مطلوب', async () => {
    const user = userEvent.setup();
    render(<ReviewForm productId={1} />);

    await user.type(screen.getByPlaceholderText('reviews.commentPlaceholder'), 'جيّد جداً');
    await submit(user);

    expect(apiMock.createReview).not.toHaveBeenCalled();
    expect(screen.getByText('reviews.ratingRequired')).toBeInTheDocument();
  });

  it('بلا نصّ لا يُرسَل، والرسالة تقول إنّ التعليق مطلوب', async () => {
    const user = userEvent.setup();
    render(<ReviewForm productId={1} />);

    await rate(user, 4);
    await submit(user);

    expect(apiMock.createReview).not.toHaveBeenCalled();
    expect(screen.getByText('reviews.commentRequired')).toBeInTheDocument();
  });

  it('مساحات وحدها ليست تعليقاً', async () => {
    const user = userEvent.setup();
    render(<ReviewForm productId={1} />);

    await rate(user, 5);
    await user.type(screen.getByPlaceholderText('reviews.commentPlaceholder'), '    ');
    await submit(user);

    expect(apiMock.createReview).not.toHaveBeenCalled();
  });

  it('يُرسل التقييم مقصوصاً ويُفرِّغ النموذج بعد النجاح', async () => {
    const user = userEvent.setup();
    const onSubmitted = vi.fn();
    render(<ReviewForm productId={42} onSubmitted={onSubmitted} />);

    await rate(user, 5);
    await user.type(screen.getByPlaceholderText('reviews.commentPlaceholder'), '  منتج ممتاز  ');
    await submit(user);

    await waitFor(() => expect(apiMock.createReview).toHaveBeenCalledWith(42, { rating: 5, comment: 'منتج ممتاز' }));
    expect(toast.success).toHaveBeenCalledWith('reviews.submitted');
    expect(onSubmitted).toHaveBeenCalled();
    expect(screen.getByPlaceholderText('reviews.commentPlaceholder')).toHaveValue('');
  });

  it('متجرٌ بالإشراف: يُقال للعميل إنّ تقييمه ينتظر المراجعة', async () => {
    const user = userEvent.setup();
    apiMock.createReview.mockResolvedValue({ status: 'Pending' });
    render(<ReviewForm productId={1} />);

    await rate(user, 4);
    await user.type(screen.getByPlaceholderText('reviews.commentPlaceholder'), 'جيّد');
    await submit(user);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('reviews.submittedPending'));
  });

  it('رفض الخادم يُعرض كما أعاده ولا يُفقد ما كُتب', async () => {
    const user = userEvent.setup();
    apiMock.createReview.mockRejectedValue(new Error('لم تشترِ هذا المنتج بعد'));
    render(<ReviewForm productId={1} />);

    await rate(user, 3);
    await user.type(screen.getByPlaceholderText('reviews.commentPlaceholder'), 'تعليقي');
    await submit(user);

    expect(await screen.findByText('لم تشترِ هذا المنتج بعد')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('reviews.commentPlaceholder')).toHaveValue('تعليقي');
    expect(screen.getByRole('button', { name: 'reviews.submit' })).toBeEnabled();
  });
});
