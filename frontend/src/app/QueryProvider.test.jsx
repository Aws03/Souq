// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { useQuery } from '@tanstack/react-query';

// ============================================================================
// أخطر ما في ذاكرة مؤقّتة داخل متجر: أن تبقى بعد تبدّل صاحبها.
//
// خروجُ زبون ودخولُ آخر على الجهاز نفسه لا يعيد تحميل الصفحة، فالذاكرة التي فيها طلبات الأول
// وعناوينه تبقى حيّة — وشاشة "طلباتي" للثاني كانت ستُرسَم منها قبل أن يُسأل الخادم أصلاً.
// الخادم لا يُخطئ هنا (كل طلب يُصرَّح على حدة)، لكنه لا يُسأل — وهذا يكفي للتسريب.
// ============================================================================
const auth = vi.hoisted(() => ({ value: { user: null } }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth.value }));

const { QueryProvider } = await import('./QueryProvider');

const fetcher = vi.fn();

function Orders() {
  const { data } = useQuery({ queryKey: ['my-orders'], queryFn: fetcher });
  return <p>{`orders: ${data ?? 'none'}`}</p>;
}

beforeEach(() => {
  auth.value = { user: { id: 1 } };
  fetcher.mockReset();
});

describe('QueryProvider', () => {
  it('يمسح الذاكرة عند تبدّل المستخدم', async () => {
    fetcher.mockResolvedValue('customer-1 orders');
    const { rerender } = render(<QueryProvider><Orders /></QueryProvider>);
    await screen.findByText('orders: customer-1 orders');

    fetcher.mockResolvedValue('customer-2 orders');
    auth.value = { user: { id: 2 } };
    rerender(<QueryProvider><Orders /></QueryProvider>);

    // لا لحظة يرى فيها الثاني بيانات الأول.
    await waitFor(() => expect(screen.getByText('orders: customer-2 orders')).toBeInTheDocument());
    expect(screen.queryByText('orders: customer-1 orders')).toBeNull();
  });

  it('يمسحها عند الخروج إلى زائر', async () => {
    fetcher.mockResolvedValue('customer-1 orders');
    const { rerender } = render(<QueryProvider><Orders /></QueryProvider>);
    await screen.findByText('orders: customer-1 orders');

    fetcher.mockReset();
    fetcher.mockImplementation(() => new Promise(() => {})); // الزائر لا يحصل على جواب
    auth.value = { user: null };
    rerender(<QueryProvider><Orders /></QueryProvider>);

    await waitFor(() => expect(screen.getByText('orders: none')).toBeInTheDocument());
  });

  it('لا يمسحها عند إعادة رسم بنفس المستخدم', async () => {
    fetcher.mockResolvedValue('customer-1 orders');
    const { rerender } = render(<QueryProvider><Orders /></QueryProvider>);
    await screen.findByText('orders: customer-1 orders');

    const callsBefore = fetcher.mock.calls.length;
    rerender(<QueryProvider><Orders /></QueryProvider>);

    expect(screen.getByText('orders: customer-1 orders')).toBeInTheDocument();
    expect(fetcher.mock.calls.length).toBe(callsBefore);
  });

  it('لا يعيد المحاولة على خطأ عميل (4xx)', async () => {
    fetcher.mockRejectedValue(Object.assign(new Error('gone'), { status: 404 }));
    render(<QueryProvider><Orders /></QueryProvider>);

    await waitFor(() => expect(fetcher).toHaveBeenCalled());
    // مهلة قصيرة تكفي لأي إعادة محاولة كانت ستقع.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
});
