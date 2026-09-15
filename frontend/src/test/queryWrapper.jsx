import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ============================================================================
// غلاف اختبار لطبقة الاستعلام: عميل جديد لكل اختبار (لا ذاكرة تتسرّب بين حالتين)،
// وبلا إعادة محاولة — اختبار خطأ يجب أن يصل إلى الشاشة فوراً لا بعد محاولتين.
// ============================================================================
export function withQueryClient(ui) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={client}>{ui}</QueryClientProvider>;
}
