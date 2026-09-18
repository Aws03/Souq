using Microsoft.EntityFrameworkCore;
using Souq.Application.Features.Products.Contracts;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// تنفيذ سياسة حفظ سجلّ البحث (M13) — حذفٌ مجمَّع، محدود الدفعة، في نطاق متجر السياق.
//
// **`ExecuteDeleteAsync` لا تحميلٌ ثم `Remove`**: جملة `DELETE` واحدة، بلا قراءةٍ ولا تتبّعٍ لآلاف الصفوف
// (نفس ما يفعله `OutboxProcessor` لرسائله المعالَجة). والسطر بيانٌ لا تجمّع، فلا قاعدةَ عملٍ تُفوَّت بذلك.
//
// **والحدّ يُطبَّق بمفتاحٍ لا بـ `Take` على الحذف**: `DELETE ... TOP` غير معبَّر عنه في LINQ، فتُقرأ معرّفات
// الدفعة أولاً (مفهرسةً بـ (المستأجر، تاريخ البحث)) ثم تُحذف بها. قراءةُ خمسة آلاف `int` رخيصة، والمكسب أنّ
// المعاملة تبقى صغيرة على جدولٍ يُكتب فيه في اللحظة نفسها.
//
// **ومرشّح المستأجر هو ما يحصر الحذف**: الحلقة في `AppDbContext` تضيفه لكل `ITenantOwned`، فهذه الجملة لا
// تلمس صفوف متجرٍ آخر — ولو نُفِّذت في نطاق المنصّة لحذفت من الجميع، ولهذا يُرسلها المنسّق داخل نطاق متجر.
// ============================================================================
internal sealed class SearchLogRetention : ISearchLogRetention
{
    private readonly AppDbContext _db;
    public SearchLogRetention(AppDbContext db) => _db = db;

    public async Task<int> PurgeBeforeAsync(DateTime before, int max, CancellationToken ct)
    {
        var ids = await _db.SearchQueryLogs.AsNoTracking()
            .Where(l => l.SearchedAt < before)
            .OrderBy(l => l.SearchedAt)
            .Select(l => l.Id)
            .Take(max)
            .ToListAsync(ct);

        if (ids.Count == 0) return 0;

        return await _db.SearchQueryLogs.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync(ct);
    }
}
