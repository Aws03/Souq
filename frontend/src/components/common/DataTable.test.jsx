// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import DataTable from './DataTable';

// ============================================================================
// الغلاف يمرّر أفقياً (overflow-x: auto) لأنّ جداول لوحة الإدارة أعرض من شاشة الهاتف.
// ومنطقةٌ تمرّر بلا تبئير لا يصلها من لا يملك فأرة: كروم وسفاري لا يمنحان عنصراً غير
// قابل للتبئير تمريراً بلوحة المفاتيح (فَيَرفُكس وحده يفعل) — WCAG 2.1.1. قِيست المخالفة
// على متصفّح حقيقي بعرض Pixel 7 (axe: scrollable-region-focusable، خطورة serious، على
// ‎/admin/reviews) ولم تظهر بعرض سطح المكتب لأنّ الجدول لا يفيض هناك — ولهذا لم يرها
// فحص axe القائم، فهو يعمل بعرض سطح المكتب وحده.
//
// jsdom بلا تخطيط: لا شيء يفيض ولا شيء يُمرَّر، فلا يستطيع هذا الملف قياس التمرير نفسه.
// ما يقيسه هو **العقد الذي يجعل الوصول ممكناً**: منطقة مسمّاة قابلة للتبئير، دائماً، لا
// بشرطٍ يعتمد على عرضٍ لحظي. وغيابُه يُمسَك في المتصفّح (axe بعرض الهاتف).
// ============================================================================
describe('DataTable — منطقة مُمرَّرة يصلها من لا يملك فأرة', () => {
  const columns = [
    { key: 'name', header: 'الاسم', render: (r) => r.name },
    { key: 'email', header: 'البريد', render: (r) => r.email },
  ];
  const rows = [{ id: 1, name: 'ريم', email: 'reem@souq.test' }];

  const table = (props = {}) => render(
    <DataTable columns={columns} rows={rows} rowKey={(r) => r.id} label="الفريق" {...props} />,
  );

  it('الجدول داخل منطقة مسمّاة قابلة للتبئير', () => {
    table();

    const region = screen.getByRole('region', { name: 'الفريق' });
    expect(region).toHaveAttribute('tabindex', '0');
    expect(region).toContainElement(screen.getByRole('table'));
  });

  it('التبئير قائم أثناء التحميل وفي الحالة الفارغة — لا يظهر بعد وصول البيانات وحدها', () => {
    const { unmount } = table({ loading: true, rows: [] });
    expect(screen.getByRole('region', { name: 'الفريق' })).toHaveAttribute('tabindex', '0');
    unmount();

    table({ rows: [] });
    expect(screen.getByRole('region', { name: 'الفريق' })).toHaveAttribute('tabindex', '0');
  });

  it('لافتة الخطأ ليست منطقة جدول: لا جدول هناك يُمرَّر', () => {
    table({ error: 'تعذّر الجلب' });
    expect(screen.queryByRole('region', { name: 'الفريق' })).toBeNull();
  });
});
