using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Security;
using Souq.Application.Features.Billing;

namespace Souq.API.Controllers;

// ============================================================================
// فوترةُ اشتراكات التجّار من المنصّة (C5، [ADR-0056](0056)).
//
// **على مضيف المنصّة وحده** (`[PlatformEndpoint]`) وبصلاحية `platform.billing.manage` — وهي
// صلاحيةُ C1 نفسها لا رابعةٌ جديدة: إصدارُ فاتورةٍ لتاجر هو العلاقةُ التجارية معه بعينها، وهي
// ما وُجدت تلك الصلاحيةُ لأجله. والمالكُ وحده يملكها (`RolePermissions`)، فمشرفُ المنصّة يرى
// المتاجر ولا يُصدر عليها مالاً.
//
// **ولا مزوّدَ دفعٍ خلف أيٍّ من هذه النقاط.** التحصيلُ حوالةٌ يسجّلها مشغّل — `C-15`.
// ============================================================================
[ApiController]
[Route("api/platform/billing")]
[Authorize]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Billing)]
public class PlatformBillingSettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformBillingSettingsController(IMediator mediator) => _mediator = mediator;

    // إعدادُ الفوترة — وفيه تدخل عملةُ الفوترة التي أجاب عنها المالك في `C-15`. القراءةُ تحمل
    // `canIssue` و`blockingReason`: زرٌّ معطَّل بلا سببٍ مكتوب يُفتح له بلاغ.
    [HttpGet("settings")]
    public async Task<IActionResult> Settings() => Ok(await _mediator.Send(new GetPlatformBillingSettingsQuery()));

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] PlatformBillingSettingsInput body) =>
        this.ToHttp(await _mediator.Send(new UpdatePlatformBillingSettingsCommand(body)));
}

[ApiController]
[Route("api/platform/invoices")]
[Authorize]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Billing)]
public class PlatformInvoicesController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformInvoicesController(IMediator mediator) => _mediator = mediator;

    // GET /api/platform/invoices?tenantId=&search=&status=&overdueOnly=&page=&pageSize=
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? tenantId, [FromQuery] string? search, [FromQuery] string? status,
        [FromQuery] bool overdueOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListPlatformInvoicesQuery(tenantId, search, status, overdueOnly, page, pageSize)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => this.ToHttp(await _mediator.Send(new GetPlatformInvoiceQuery(id)));

    // مسوّدةٌ جديدة. تبدأ بلا رقم ولا وجودَ محاسبيّ — الإصدارُ فعلٌ منفصل.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest body)
    {
        var result = await _mediator.Send(new CreatePlatformInvoiceCommand(
            body.TenantId, body.PeriodStartUtc, body.PeriodEndUtc,
            body.IncludeSubscription, body.BillingPeriodId, body.Lines));
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, new { id = result.Value })
            : this.Failure(result);
    }

    [HttpPost("{id:int}/lines")]
    public async Task<IActionResult> AddLine(int id, [FromBody] InvoiceLineInput body)
    {
        var result = await _mediator.Send(new AddPlatformInvoiceLineCommand(id, body));
        return result.IsSuccess ? Ok(new { id = result.Value }) : this.Failure(result);
    }

    [HttpDelete("{id:int}/lines/{lineId:int}")]
    public async Task<IActionResult> RemoveLine(int id, int lineId) =>
        this.ToHttp(await _mediator.Send(new RemovePlatformInvoiceLineCommand(id, lineId)));

    // تحميلُ ما قيس من فترةٍ **مغلقة** على المسوّدة، بسعرِ وحدةٍ يُمرّره المشغّل: الآلةُ تعرف كم
    // قيس ولا تعرف بكم يُباع (وهو `C-12`، وما يزال المالك).
    [HttpPost("{id:int}/metered-lines")]
    public async Task<IActionResult> AddMeteredLines(int id, [FromBody] MeteredLinesRequest body)
    {
        var result = await _mediator.Send(new AddMeteredLinesCommand(
            id, body.BillingPeriodId, body.Meter, body.UnitAmount, body.Description));
        return result.IsSuccess ? Ok(new { id = result.Value }) : this.Failure(result);
    }

    [HttpPut("{id:int}/notes")]
    public async Task<IActionResult> UpdateNotes(int id, [FromBody] NotesRequest body) =>
        this.ToHttp(await _mediator.Send(new UpdatePlatformInvoiceNotesCommand(id, body?.Notes)));

    // **الإصدار**: يسحب الرقم، ويُجمّد لقطةَ الضريبة والمُصدِرَ والمُرسَلَ إليه، ويُغلق التحرير.
    [HttpPost("{id:int}/issue")]
    public async Task<IActionResult> Issue(int id, [FromBody] IssueInvoiceRequest? body)
    {
        var result = await _mediator.Send(new IssuePlatformInvoiceCommand(id, body?.BilledToTaxNumber));
        return result.IsSuccess ? Ok(new { number = result.Value }) : this.Failure(result);
    }

    // إلغاءُ **مسوّدة**. الفاتورةُ الصادرة لا تُلغى — تُقابَل بإشعار دائن (يرفضه المجال بـ 422).
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id) =>
        this.ToHttp(await _mediator.Send(new CancelPlatformInvoiceDraftCommand(id)));

    // تسجيلُ سدادٍ وقع خارج النظام: حوالةٌ أو نقدٌ أو شيك، يراه المشغّل في كشفه فيسجّله.
    [HttpPost("{id:int}/payments")]
    public async Task<IActionResult> RecordPayment(int id, [FromBody] RecordPaymentRequest body) =>
        this.ToHttp(await _mediator.Send(new RecordInvoicePaymentCommand(
            id, body.Amount, body.Method, body.ReceivedAtUtc, body.Reference, body.Note)));

    // إشعارُ دائن: التصحيحُ الوحيد الممكن على مستندٍ صدر.
    [HttpPost("{id:int}/credit-notes")]
    public async Task<IActionResult> IssueCreditNote(int id, [FromBody] CreditNoteRequest body)
    {
        var result = await _mediator.Send(new IssueCreditNoteCommand(id, body.Reason, body.Lines));
        return result.IsSuccess ? Ok(new { number = result.Value }) : this.Failure(result);
    }

    public sealed record CreateInvoiceRequest(
        int TenantId, DateTime PeriodStartUtc, DateTime PeriodEndUtc,
        bool IncludeSubscription, int? BillingPeriodId, IReadOnlyList<InvoiceLineInput>? Lines);

    public sealed record MeteredLinesRequest(int BillingPeriodId, string Meter, decimal UnitAmount, string? Description);

    public sealed record NotesRequest(string? Notes);

    public sealed record IssueInvoiceRequest(string? BilledToTaxNumber);

    public sealed record RecordPaymentRequest(
        decimal Amount, string Method, DateTime ReceivedAtUtc, string? Reference, string? Note);

    public sealed record CreditNoteRequest(string Reason, IReadOnlyList<InvoiceLineInput> Lines);
}

// ============================================================================
// قياسُ متجرٍ بعينه وفتراتُه. المسارُ يحمل معرّف المتجر — امتيازُ منطقة المنصّة وحدها، ويثبته
// `كل_مجلّد_في_منطقة_المنصّة_يُخدَم_على_مضيفها_وحده`.
// ============================================================================
[ApiController]
[Route("api/platform/tenants/{tenantId:int}/billing")]
[Authorize]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Billing)]
public class PlatformTenantMeteringController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformTenantMeteringController(IMediator mediator) => _mediator = mediator;

    [HttpGet("periods")]
    public async Task<IActionResult> Periods(int tenantId) =>
        Ok(await _mediator.Send(new ListBillingPeriodsQuery(tenantId)));

    [HttpGet("periods/{periodId:int}/events")]
    public async Task<IActionResult> Events(int tenantId, int periodId) =>
        Ok(await _mediator.Send(new ListBillableEventsQuery(tenantId, periodId)));

    // إغلاقُ الفترة: تتوقّف عن قبول الأحداث ثم تصير نهائية. لا يلتحق بها شيءٌ بعدها أبداً.
    [HttpPost("periods/{periodId:int}/close")]
    public async Task<IActionResult> ClosePeriod(int tenantId, int periodId) =>
        this.ToHttp(await _mediator.Send(new CloseBillingPeriodCommand(tenantId, periodId)));

    // تسجيلُ وحدةٍ قابلة للفوترة. **غيرُ مكرِّرة بمفتاحها**: إعادةُ الإرسال تعيد المعرّف القائم.
    [HttpPost("events")]
    public async Task<IActionResult> RecordEvent(int tenantId, [FromBody] RecordEventRequest body)
    {
        var result = await _mediator.Send(new RecordBillableEventCommand(
            tenantId, body.Meter, body.Quantity, body.OccurredAtUtc, body.IdempotencyKey, body.Description));
        return result.IsSuccess ? Ok(new { id = result.Value }) : this.Failure(result);
    }

    public sealed record RecordEventRequest(
        string Meter, decimal Quantity, DateTime OccurredAtUtc, string IdempotencyKey, string? Description);
}
