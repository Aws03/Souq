// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

// ============================================================================
// قراءةٌ فاشلة للسلّة ليست سلّةً فارغة.
//
// كان `reload` يبتلع كل إخفاق (`.catch(() => {})`) ويُعلن السلّة **محمَّلة** على
// `EMPTY_BASKET`. فقرأت الشاشات ذلك كما هو مكتوب: صفحة الدفع تقول «أضف منتجات أولاً»
// لمشترٍ سلّته على الخادم فيها أصنافه — فيُعيد الشراء أو ينصرف، وأصنافه سليمة.
//
// وهو أيضاً إخفاق `checkout-reliability` المتقطّع (TD-57): تحت الحِمل تفشل قراءة واحدة،
// فتختفي خانة العنوان لأنّ الصفحة تعرض حالة الفراغ. لا عيب في الرحلة — عيبٌ فيما تراه.
//
// المطلوب ثلاثة، ولذلك ثلاثة اختبارات: أن تُعاد القراءة مرّة (العملية عديمة الأثر وأغلب
// الإخفاقات عابرة)، وألّا يُعلَن الفراغ حين يُصرّ الإخفاق، وأن تُصفَّر الحالة عند النجاح
// كي لا يبقى الخطأ بعد أن يزول سببه.
// ============================================================================
const apiMock = vi.hoisted(() => ({ getBasket: vi.fn() }));
vi.mock('../api/client', () => ({ api: apiMock }));
vi.mock('react-i18next', () => ({ useTranslation: () => ({ i18n: { language: 'en' } }) }));
vi.mock('./AuthContext', () => ({ useAuth: () => ({ user: null, loading: false }) }));
vi.mock('./ToastContext', () => ({ useToast: () => ({ error: vi.fn() }) }));

const { CartProvider, useCart } = await import('./CartContext');

// الشكل كما يقرأه `toCartItems`: الأسطر في `lines`، لا في `items`.
const FULL = {
  currency: 'JOD', subtotal: 10, shipping: 0, total: 10, itemCount: 1,
  lines: [{ productId: 1, variantId: 1, name: 'thing', quantity: 1, unitPrice: 10, lineTotal: 10, sellable: true, available: 5 }],
};

function Probe() {
  const { loaded, loadFailed, items, reload } = useCart();
  return (
    <div>
      <span data-testid="state">{`loaded=${loaded} failed=${loadFailed} items=${items.length}`}</span>
      <button type="button" onClick={reload}>reload</button>
    </div>
  );
}

const state = () => screen.getByTestId('state').textContent;
const renderCart = () => render(<CartProvider><Probe /></CartProvider>);

describe('قراءة السلّة حين تفشل', () => {
  beforeEach(() => { apiMock.getBasket.mockReset(); });

  it('إخفاق عابر يُعاد ولا يُرى: القراءة الثانية تنجح فتظهر الأصناف', async () => {
    apiMock.getBasket.mockRejectedValueOnce(new Error('boom')).mockResolvedValue(FULL);

    renderCart();

    await waitFor(() => expect(state()).toBe('loaded=true failed=false items=1'));
    expect(apiMock.getBasket).toHaveBeenCalledTimes(2);
  });

  it('إخفاق مُصرّ عليه يُعلَن خطأً — ولا يُقدَّم بوصفه سلّة فارغة', async () => {
    apiMock.getBasket.mockRejectedValue(new Error('boom'));

    renderCart();

    await waitFor(() => expect(state()).toBe('loaded=true failed=true items=0'));
    expect(apiMock.getBasket).toHaveBeenCalledTimes(2);
  });

  it('السلّة الفارغة حقّاً تبقى فارغة بلا خطأ — وإلّا صار كل متجر جديد عُطلاً', async () => {
    apiMock.getBasket.mockResolvedValue({ ...FULL, lines: [], itemCount: 0, subtotal: 0, total: 0 });

    renderCart();

    await waitFor(() => expect(state()).toBe('loaded=true failed=false items=0'));
    expect(apiMock.getBasket).toHaveBeenCalledTimes(1);
  });
});
