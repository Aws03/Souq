using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// الصفحة الواحدة لكل قوائم القراءة. التوقيع نفسه يفرض القاعدتين المهمّتين:
//   IOrderedQueryable — لا صفحة بلا ترتيب (بلا ORDER BY يعيد SQL Server الصفوف بأي ترتيب،
//                       فتتكرّر أو تختفي عناصر بين الصفحات). كاسر التعادل بالمعرّف مسؤولية
//                       المستدعي، ويثبته اختبار تكامل.
//   projection        — لا صفحة بلا إسقاط صريح: أعمدة الـ DTO فقط، لا كيانات كاملة (D3).
// استعلامان صغيران (العدد ثم الصفحة) بدل تحميل الجدول؛ صفحة فارغة لا تكلّف استعلاماً ثانياً.
// ============================================================================
internal static class QueryableExtensions
{
    public static async Task<PaginatedList<TResult>> ToPageAsync<TSource, TResult>(
        this IOrderedQueryable<TSource> ordered, Expression<Func<TSource, TResult>> projection,
        PageRequest page, CancellationToken ct)
    {
        var total = await ordered.CountAsync(ct);
        var items = total == 0
            ? []
            : await ordered.Skip(page.Skip).Take(page.PageSize).Select(projection).ToListAsync(ct);
        return new PaginatedList<TResult>(items, total, page.Page, page.PageSize);
    }
}
