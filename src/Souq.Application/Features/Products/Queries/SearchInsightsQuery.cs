using FluentValidation;
using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// ما بحث عنه المتسوّقون، مُجمَّعاً لشاشة التاجر (M13).
//
// **الوحدة هي الكلمة لا البحث**: التاجر لا يقرأ ألف سطر، بل يسأل "أيّ كلمةٍ تتكرّر ولا تجد شيئاً". فالتجميع
// على (اللغة، الصورة المطبَّعة) — بادئة الفهرس نفسها — والعدّ في القاعدة لا في الذاكرة.
//
// **ويُعرض ما كُتب فعلاً مع الصورة المطبَّعة**: التجميع بالثانية (فـ"مكنسه" و"مكنسة" صفٌّ واحد)، والعرض
// بالأولى كي يقرأ التاجر كلام زبائنه لا صورةً هندسية. وهي أحدث صورةٍ كُتبت، لأنّ الأحدث أقرب إلى ما يُكتب الآن.
//
// **ونافذةٌ زمنية إلزامية**: "أكثر ما يُبحث" بلا زمنٍ سؤالٌ بلا معنى — كلمةٌ سادت في رمضان تبقى أولاً في يناير.
// والنافذة محدودةٌ بمدّة الحفظ (SearchLogSettings.RetentionDays): طلبُ ما هو أقدم من المحفوظ يُعيد نصف حقيقة.
// ============================================================================
public sealed record SearchTermInsightDto(
    string Culture,
    string Term,
    string TermNormalized,
    int Searches,
    int ZeroResultSearches,
    DateTime LastSearchedAt)
{
    // الكلمة التي لم تجد شيئاً **في كل مرّة**: هي وحدها التي تستحقّ مرادفاً أو منتجاً، وليست الكلمة التي
    // تفشل أحياناً (نفاد مخزون مؤقّت يُنتج هذه). التمييز هو ما يمنع الشاشة من إغراق التاجر بعملٍ وهمي.
    public bool NeverFoundAnything => Searches > 0 && ZeroResultSearches == Searches;
}

// ملخّص النافذة كلّها — الرقم الذي يُقرأ أولاً: هل البحث في هذا المتجر يعمل؟
public sealed record SearchInsightsSummary(int TotalSearches, int DistinctTerms, int ZeroResultSearches)
{
    public static readonly SearchInsightsSummary Empty = new(0, 0, 0);
}

// صفحةٌ وملخّص معاً — نفس تركيب `ProductSearchPage`، ولنفس السبب: شكل الترقيم لا يُكرَّر في نوع ثانٍ.
public sealed class SearchInsightsPage : PaginatedList<SearchTermInsightDto>
{
    public SearchInsightsSummary Summary { get; }

    public SearchInsightsPage(PaginatedList<SearchTermInsightDto> page, SearchInsightsSummary summary)
        : base(page.Items, page.TotalCount, page.PageNumber, page.PageSize) => Summary = summary;
}

// Culture فارغة ⇒ كل اللغات. OnlyZeroResults ⇒ الكلمات التي لم تجد شيئاً ولا مرّة (وهي شاشة العمل).
public sealed record SearchInsightFilter(string? Culture, DateTime Since, bool OnlyZeroResults);

// حدود النافذة — في صنفها لا في السجلّ، لأنّ قيمةً افتراضية لا تُقرأ من ثابتٍ يُعرَّف في نفس السجلّ.
public static class SearchInsightRules
{
    // شهرٌ افتراضاً: أقصر ممّا يُخفي موسماً، وأطول ممّا يجعل كلمةً عارضة تبدو نمطاً.
    public const int DefaultDays = 30;
    public const int MaxDays = 365;
}

public sealed record SearchInsightsQuery(
    string? Culture = null,
    int Days = SearchInsightRules.DefaultDays,
    bool OnlyZeroResults = false,
    int Page = 1,
    int PageSize = 20
) : IRequest<SearchInsightsPage>, IPagedQuery;

// التحقّق الشكلي: قواعد الترقيم الموحّدة، والنافذة يوماً واحداً على الأقل. المدى الأعلى لا يُرفض بل يُقصّ
// في المعالج على مدّة الحفظ — فسنةٌ مطلوبةٌ من حفظٍ تسعينيّ ليست خطأ تاجر.
public sealed class SearchInsightsQueryValidator : PagedQueryValidator<SearchInsightsQuery>
{
    public SearchInsightsQueryValidator()
    {
        RuleFor(x => x.Days).InclusiveBetween(1, SearchInsightRules.MaxDays);
        RuleFor(x => x.Culture).MaximumLength(CatalogTranslation.CultureMaxLength);
    }
}

public class SearchInsightsHandler : IRequestHandler<SearchInsightsQuery, SearchInsightsPage>
{
    private readonly ICatalogQueries _catalog;
    private readonly SearchLogSettings _settings;
    private readonly TimeProvider _clock;

    public SearchInsightsHandler(ICatalogQueries catalog, SearchLogSettings settings, TimeProvider clock)
    {
        _catalog = catalog; _settings = settings; _clock = clock;
    }

    public Task<SearchInsightsPage> Handle(SearchInsightsQuery q, CancellationToken ct)
    {
        // النافذة تُقصّ على مدّة الحفظ لا تُرفض: التاجر الذي يطلب سنةً والحفظ تسعون يوماً يستحقّ التسعين،
        // لا خطأً. والقصّ هنا لا في القاعدة كي يبقى المعروض موافقاً لما هو محفوظ فعلاً.
        var days = Math.Clamp(q.Days, 1, Math.Min(SearchInsightRules.MaxDays, _settings.RetentionDays));
        var since = _clock.GetUtcNow().UtcDateTime.AddDays(-days);

        return _catalog.ListSearchInsightsAsync(
            new SearchInsightFilter(q.Culture, since, q.OnlyZeroResults), PageRequest.From(q), ct);
    }
}
