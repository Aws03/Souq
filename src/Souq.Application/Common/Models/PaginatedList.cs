namespace Souq.Application.Common.Models;

// ============================================================================
// PaginatedList — لماذا الترقيم إلزامي؟ (مبدأ غير وظيفي: الأداء وقابلية التوسّع)
// إرجاع 10,000 منتج دفعة واحدة يقتل الأداء ويرهق الشبكة والمتصفّح. نُرجع صفحة
// واحدة فقط مع بيانات تكفي الواجهة لبناء أزرار التنقّل (الصفحة، الإجمالي...).
// ============================================================================
public class PaginatedList<T>
{
    public IReadOnlyList<T> Items { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalCount { get; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => PageNumber < TotalPages;
    public bool HasPrevious => PageNumber > 1;

    public PaginatedList(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items; TotalCount = totalCount; PageNumber = pageNumber; PageSize = pageSize;
    }
}
