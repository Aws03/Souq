using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;

namespace Souq.Application.Features.Reporting;

// ============================================================================
// إيراد المنصّة عبر المتاجر (C11) — أوّل رقم **مالٍ** يعبر من متجر إلى شاشة المنصّة.
//
// **وهذا يعكس قراراً مكتوباً، فيُقال صراحةً.** وثيقة وحدة Reporting كانت تقول: "لا تصل أرقام
// متجر التجارية إلى المنصّة بالتصميم". كان ذلك صحيحاً لمنصّة لا تبيع شيئاً؛ وهو غير ممكن لمنصّة
// **تُفوتر** تُجّارها: عمولةٌ بلا معرفة المبيعات لا تُحسب، وقرارات المالك C-03 (أساس العمولة)
// تفترض هذا الرقم موجوداً. CommercialPlatformArchitecture §4.15 قرّر ذلك، وهذا تنفيذه.
//
// وما لم يتغيّر: القراءة تبقى في `PlatformQueries` — الصنف المُراجَع الوحيد المسموح له بتجاوز
// مرشّح المستأجر — وخلف `platform.reports.view`، ومُدقَّقة كأي طلب في منطقة المنصّة.
//
// **وما يُعرَض على التاجر في عقده مسألةُ إفصاحٍ لا هندسة**: أنّ مشغّل المنصّة يرى إجمالي مبيعات
// متجره يجب أن يكون في اتفاقية التاجر. مُسجَّل كنقطة مالك، لا مفترضاً هنا.
// ============================================================================

// ============================================================================
// **لا مجموع واحد عبر العملات.** كل متجر يُسعّر بعملته هو، وجمعُ عملتين مختلفتين في رقم
// واحد يُنتج عدداً بلا وحدة — وهو بالضبط نوع "الرؤية" التي يحرّمها هذا المستودع على نفسه: رقمٌ
// يبدو ذا معنى وليس له معنى. فالمجاميع **لكل عملة**، وكل متجر برقمه بعملته هو.
//
// وتحويلُها إلى عملة واحدة يحتاج أسعار صرف بتواريخها — مصدرَ بياناتٍ لا وجود له هنا، واختراعُه
// أسوأ من عدم الجمع.
// ============================================================================
public sealed record CurrencyTotalDto(string Currency, decimal Revenue, int Orders);

public sealed record StoreRevenueDto(
    int TenantId, string Slug, string Name, string Currency, decimal Revenue, int Orders);

public sealed record PlatformRevenueDto(
    DateTime From, DateTime To,
    IReadOnlyList<CurrencyTotalDto> Totals,
    IReadOnlyList<StoreRevenueDto> ByStore);

public record GetPlatformRevenueQuery(int Days = 30) : IRequest<PlatformRevenueDto>, IAuditable
{
    // مُدقَّق، ومدّته في السجلّ: قراءةُ مشغّل المنصّة لأرقام مبيعات تُجّاره حدثٌ يُسجَّل بوضوح.
    public AuditRecord ToAuditRecord() =>
        new("platform.revenue.viewed", Metadata: new Dictionary<string, object?> { ["days"] = Days });
}

public sealed class GetPlatformRevenueQueryValidator : AbstractValidator<GetPlatformRevenueQuery>
{
    // مدّة من العميل، فلها حدّ: بلا سقف يصير `?days=100000` مسحاً لجدول الطلبات كلّه.
    public GetPlatformRevenueQueryValidator() => RuleFor(x => x.Days).InclusiveBetween(1, 366);
}

public class GetPlatformRevenueHandler : IRequestHandler<GetPlatformRevenueQuery, PlatformRevenueDto>
{
    private readonly IPlatformReports _reports;
    private readonly TimeProvider _clock;

    public GetPlatformRevenueHandler(IPlatformReports reports, TimeProvider clock)
    {
        _reports = reports; _clock = clock;
    }

    // ========================================================================
    // النافذة بـ UTC هنا، بخلاف لوحة المتجر التي تُحسب بمنطقة متجرها (C11).
    //
    // وهذا مقصود لا سهو: هذه النافذة تعبر متاجر في مناطق مختلفة، فلا "يوم" واحد يصحّ لها جميعاً —
    // اختيار منطقة أحدها يجعل الرقم صحيحاً له وخاطئاً لغيره. UTC هي الاتفاق الوحيد المحايد،
    // والحدود تُعاد في الجواب كي يقرأها المشغّل كما هي لا كما يفترضها.
    // ========================================================================
    public Task<PlatformRevenueDto> Handle(GetPlatformRevenueQuery query, CancellationToken ct)
    {
        var to = _clock.GetUtcNow().UtcDateTime;
        return _reports.GetRevenueAsync(to.AddDays(-query.Days), to, ct);
    }
}
