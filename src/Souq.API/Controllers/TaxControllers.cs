using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Security;
using Souq.Application.Features.Tax;

namespace Souq.API.Controllers;

// ============================================================================
// ملفّات الضريبة من المنصّة ([ADR-0055](0055)، قرار المالك P-06): إنشاءُ ملفّ اختصاص، ومسوّدةُ
// إصدارٍ بقيمه، ونشرُه، **وتسجيلُ تحقّقٍ مهنيّ منه** — وهو الفعل الوحيد الذي يسمح بجمع ضريبة.
//
// **الصلاحية `platform.settings.manage`** — للمالك وحده، وهذه **أوّل نقطةٍ تطلبها** (كان القرار
// المفتوح P-07 يسجّل أنّ لا نقطةَ تطلبها وأنّ لا إعدادَ منصّةٍ يوجد). واختيارُها لا اختراعُ صلاحيةٍ
// رابعة مقصود: ملفُّ الاختصاص إعدادُ منصّةٍ بمعنى الكلمة — قيمةٌ واحدة تخدم كل المتاجر. ويومَ يصير
// التحقّق عملَ دورٍ ماليّ منفصل عن مالك المنصّة، يصير صلاحيةً خاصّةً به؛ وذلك قرارُ أدوار لا
// قرارُ نقطة.
// ============================================================================
[ApiController]
[Route("api/platform/tax/profiles")]
[Authorize]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Settings)]
public class PlatformTaxProfilesController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformTaxProfilesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _mediator.Send(new ListTaxProfilesQuery()));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => this.ToHttp(await _mediator.Send(new GetTaxProfileQuery(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaxProfileCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? Created($"/api/platform/tax/profiles/{result.Value}", new { id = result.Value })
            : this.Failure(result);
    }

    // مسوّدةٌ بقيمها. لا تُجمَع بها ضريبةٌ ولو نُشرت: النشر تجميدٌ، والجمعُ يحتاج تحقّقاً.
    [HttpPost("{id:int}/versions")]
    public async Task<IActionResult> AddVersion(int id, [FromBody] TaxProfileVersionInput version)
    {
        var result = await _mediator.Send(new AddTaxProfileVersionCommand(id, version));
        return result.IsSuccess ? Created($"/api/platform/tax/profiles/{id}", new { id = result.Value })
            : this.Failure(result);
    }

    [HttpPost("{id:int}/versions/{versionId:int}/publish")]
    public async Task<IActionResult> Publish(int id, int versionId)
    {
        var result = await _mediator.Send(new PublishTaxProfileVersionCommand(id, versionId));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // تسجيلُ التحقّق. الجسمُ يحمل **مَن** تحقّق: هذا ليس إقراراً من النظام بصحّة الأرقام، بل
    // تسجيلٌ لمن أقرّها — والفرقُ بينهما هو كلُّ ما يعنيه قرار المالك بـ«إعدادٌ يحتاج تحقّقاً».
    [HttpPost("{id:int}/versions/{versionId:int}/verify")]
    public async Task<IActionResult> Verify(int id, int versionId, [FromBody] VerifyTaxRequest body)
    {
        var result = await _mediator.Send(
            new VerifyTaxProfileVersionCommand(id, versionId, body.VerifiedBy, body.Note));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // سحبُ التحقّق: يتوقّف الجمع فوراً، ولا يُمسّ طلبٌ احتُسب سابقاً — لقطاتُه تحمل ما طُبِّق.
    [HttpPost("{id:int}/versions/{versionId:int}/require-confirmation")]
    public async Task<IActionResult> RequireConfirmation(int id, int versionId, [FromBody] TaxNoteRequest? body)
    {
        var result = await _mediator.Send(new RequireTaxConfirmationCommand(id, versionId, body?.Note));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    public sealed record VerifyTaxRequest(string VerifiedBy, string? Note);
    public sealed record TaxNoteRequest(string? Note);
}

// ============================================================================
// ضريبةُ المتجر كما يراها التاجر ويضبطها: **اختيارُ ملفّ** وتفعيلُ الجمع ورقمُ تسجيله — ولا نسبة.
//
// وقراءتُه تحمل **سببَ** عدم الجمع إن لم يُجمَع، فصفرُ الضريبة جوابٌ لا صمت: لم يختر ملفّاً، أو
// اختار ولم يفعّل، أو فعّل وملفُّه ينتظر تحقّقاً مهنياً.
// ============================================================================
[ApiController]
[Route("api/admin/store/tax")]
[Authorize]
[HasPermission(Permissions.Store.Settings)]
public class StoreTaxController : ControllerBase
{
    private readonly IMediator _mediator;
    public StoreTaxController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _mediator.Send(new GetStoreTaxSettingsQuery()));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] StoreTaxSettingsInput settings) =>
        this.ToHttp(await _mediator.Send(new UpdateStoreTaxSettingsCommand(settings)));

    // ملفّاتُ الاختصاص التي يستطيع المتجر اختيارها. يقرؤها بصلاحية إعداداته: لا يستطيع تعديلَها،
    // وهو يحتاج أن يرى ما يختار منه — **وأن يرى أيُّها متحقَّقٌ منه وأيُّها ينتظر**.
    [HttpGet("profiles")]
    public async Task<IActionResult> Profiles() => Ok(await _mediator.Send(new ListTaxProfilesQuery()));
}
