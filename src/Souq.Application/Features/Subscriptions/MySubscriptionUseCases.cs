using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing;

namespace Souq.Application.Features.Subscriptions;

// ============================================================================
// اشتراكُ التاجر كما يراه هو (C5، [ADR-0056](0056)): خطتُه، وما عليه، وفواتيرُه، وكيف يدفع.
//
// ============================================================================
// **ولماذا مجلّدٌ ثانٍ لوحدة Billing بدل أن يعيش هذا مع بقيّتها؟**
//
// لأنّ `Features/Billing` **منطقةُ منصّة**: طلباتُها تحمل `TenantId` من جسم الطلب، وثمنُ ذلك
// الامتياز أنّ `كل_مجلّد_في_منطقة_المنصّة_يُخدَم_على_مضيفها_وحده` يمنع خدمةَ أيٍّ منها على مضيف
// متجر. وما هنا يُخدَم على مضيف المتجر بالضرورة — فالتاجر يقرأ فاتورتَه من لوحته.
//
// فالمجلّدان وجهان لحدٍّ واحد: **Billing دفترُ المنصّة عن التاجر، وSubscriptions ما يراه التاجرُ
// من نفسه**. وكلاهما وحدة Billing في `ModuleMap`، وكلاهما مُدقَّق لأنّ الموضوع مال.
//
// **ولا `TenantId` في أيّ طلبٍ هنا إطلاقاً.** المتجر يأتي من المضيف كما في كل نقطة متجر —
// وهو ما يجعل «فاتورةُ تاجرٍ آخر» غيرَ قابلة للطلب أصلاً، لا مرفوضةً بفحص.
// ============================================================================

// ملخّصُ الاشتراك: خطتُه وسعرُها، وما عليه، وتعليماتُ الدفع. ما يُقرأ في أعلى الشاشة.
public record GetMySubscriptionQuery : IRequest<MySubscriptionDto>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("subscription.summary.viewed");
}

public class GetMySubscriptionHandler : IRequestHandler<GetMySubscriptionQuery, MySubscriptionDto>
{
    private readonly IPlatformBillingQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public GetMySubscriptionHandler(
        IPlatformBillingQueries queries, ITenantContext tenant, TimeProvider clock)
    {
        _queries = queries; _tenant = tenant; _clock = clock;
    }

    public Task<MySubscriptionDto> Handle(GetMySubscriptionQuery q, CancellationToken ct) =>
        _queries.GetSubscriptionSummaryAsync(
            _tenant.RequireTenant().Id, _clock.GetUtcNow().UtcDateTime, ct);
}

public record ListMyInvoicesQuery(string? Status = null, int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<PlatformInvoiceSummaryDto>>, IPagedQuery, IAuditable
{
    public AuditRecord ToAuditRecord() => new("subscription.invoices.listed");
}

public sealed class ListMyInvoicesValidator : PagedQueryValidator<ListMyInvoicesQuery>
{
    public ListMyInvoicesValidator() =>
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<Souq.Domain.Platform.PlatformInvoiceStatus>(s, true, out _))
            .WithMessage("حالةُ الفاتورة غير معروفة");
}

public class ListMyInvoicesHandler : IRequestHandler<ListMyInvoicesQuery, PaginatedList<PlatformInvoiceSummaryDto>>
{
    private readonly IPlatformBillingQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public ListMyInvoicesHandler(IPlatformBillingQueries queries, ITenantContext tenant, TimeProvider clock)
    {
        _queries = queries; _tenant = tenant; _clock = clock;
    }

    public Task<PaginatedList<PlatformInvoiceSummaryDto>> Handle(ListMyInvoicesQuery q, CancellationToken ct)
    {
        Souq.Domain.Platform.PlatformInvoiceStatus? status =
            Enum.TryParse<Souq.Domain.Platform.PlatformInvoiceStatus>(q.Status, true, out var parsed) ? parsed : null;

        // ============================================================================
        // **المسوّداتُ لا تُرى.** المتجرُ يرى ما صدر إليه؛ ومسوّدةٌ يحرّرها مشغّلٌ الآن ليست
        // مطالبةً بعد، وإظهارُها كان سيعني أنّ تاجراً يقرأ مبلغاً ثمّ يتغيّر قبل أن يصدر —
        // وذلك أسوأُ من ألّا يراه.
        //
        // والترشيحُ يقع **في الاستعلام لا بعده**: ترشيحٌ بعد الترقيم يُنتج صفحاتٍ ناقصة.
        // ============================================================================
        var filter = new PlatformInvoiceListFilter(
            _tenant.RequireTenant().Id, null, status, OverdueOnly: false) { IssuedOnly = true };

        return _queries.ListInvoicesAsync(filter, PageRequest.From(q), _clock.GetUtcNow().UtcDateTime, ct);
    }
}

// ============================================================================
// فاتورةٌ بعينها كما يقرؤها صاحبُها. معرّفُ المتجر يُمرَّر إلى الاستعلام **دائماً**، فمعرّفٌ
// مخمَّن لفاتورة تاجرٍ آخر يُجيب «غير موجودة» — 404 لا 403، وهو عُرفُ المستودع كلّه: لا نكشف
// وجودَ موارد الآخرين.
// ============================================================================
public record GetMyInvoiceQuery(int InvoiceId) : IRequest<Result<PlatformInvoiceDetailDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("subscription.invoice.viewed", "PlatformInvoice", InvoiceId.ToString());
}

public class GetMyInvoiceHandler : IRequestHandler<GetMyInvoiceQuery, Result<PlatformInvoiceDetailDto>>
{
    private readonly IPlatformBillingQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public GetMyInvoiceHandler(IPlatformBillingQueries queries, ITenantContext tenant, TimeProvider clock)
    {
        _queries = queries; _tenant = tenant; _clock = clock;
    }

    public async Task<Result<PlatformInvoiceDetailDto>> Handle(GetMyInvoiceQuery q, CancellationToken ct)
    {
        var invoice = await _queries.GetInvoiceAsync(
            q.InvoiceId, _tenant.RequireTenant().Id, _clock.GetUtcNow().UtcDateTime, ct);

        // ومسوّدةٌ لم تصدر — أو أُلغيت قبل أن تصدر — لا تُقرأ حتى بمعرّفها الصحيح: ليست مستنداً.
        // القائمةُ **بيضاء** لا سوداء: حالةٌ جديدة تُضاف يوماً لا تصير مرئيّةً للتاجر بالسهو.
        var visible = invoice?.Status is nameof(Souq.Domain.Platform.PlatformInvoiceStatus.Issued)
            or nameof(Souq.Domain.Platform.PlatformInvoiceStatus.Settled);

        return visible
            ? Result<PlatformInvoiceDetailDto>.Success(invoice!)
            : Result<PlatformInvoiceDetailDto>.Failure(Error.NotFound("الفاتورة غير موجودة"));
    }
}
