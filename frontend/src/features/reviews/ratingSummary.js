// ============================================================================
// ملخّص تقييمات منتج (المرحلة 13): الخادم يعيد العدد والمتوسط والتوزيع على النجوم من التقييمات المعتمدة وحدها. هنا تحويل
// التوزيع لأشرطة بنسبة من المجموع (كل نجمة حاضرة ولو صفراً)، ومفتاح رسالة ما بعد الإرسال: منشور الآن أم بانتظار مراجعة المتجر.
// منطق خالص مُختبَر بـ Vitest.
// ============================================================================
export function distributionRows(distribution, total) {
  const counts = new Map((Array.isArray(distribution) ? distribution : []).map((d) => [d.rating, d.count]));
  return [5, 4, 3, 2, 1].map((rating) => {
    const count = counts.get(rating) ?? 0;
    return { rating, count, percent: total > 0 ? Math.round((count / total) * 100) : 0 };
  });
}

export const submittedMessageKey = (status) => (status === 'Pending' ? 'reviews.submittedPending' : 'reviews.submitted');
