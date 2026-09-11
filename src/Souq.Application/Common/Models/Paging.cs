using FluentValidation;

namespace Souq.Application.Common.Models;

// ============================================================================
// الترقيم الموحّد لكل قوائم الـ API (ADR-0008):
//   IPagedQuery         — كل استعلام قائمة يعلن Page/PageSize بالأسماء نفسها (page/pageSize في HTTP)
//   PagedQueryValidator — القواعد مرّة واحدة: صفحة ≥ 1، حجم 1..MaxPageSize (Phase 0 C9)
//   PageRequest         — ما يعبر إلى خدمة القراءة في Infrastructure بدل عددين عاريين
// الترتيب والتصفية صريحان لكل مورد (enum قائمة مسموحة + معايير مطبوعة) — لا إطار استعلام
// عام، ولا IQueryable يعبر حدود Application أبداً.
// ============================================================================
public interface IPagedQuery
{
    int Page { get; }
    int PageSize { get; }
}

public readonly record struct PageRequest(int Page, int PageSize)
{
    public int Skip => (Page - 1) * PageSize;

    public static PageRequest From(IPagedQuery query) => new(query.Page, query.PageSize);
}

public abstract class PagedQueryValidator<T> : AbstractValidator<T> where T : IPagedQuery
{
    protected PagedQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagingRules.MaxPageSize);
    }
}

public static class PaginatedListExtensions
{
    // تحويل عناصر الصفحة مع الإبقاء على بيانات الترقيم (صفوف SQL ⇒ DTO في الذاكرة).
    public static PaginatedList<TOut> Map<TIn, TOut>(this PaginatedList<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.TotalCount, page.PageNumber, page.PageSize);
}
