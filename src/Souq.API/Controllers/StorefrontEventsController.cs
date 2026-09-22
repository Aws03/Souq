using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Souq.Application.Features.Analytics;
using Souq.API.Security;

namespace Souq.API.Controllers;

// ============================================================================
// التقاطُ ما لا يقع إلّا في المتصفّح (C9b، ADR-0050 §3): الظهورُ في قائمة، والنقرُ عليها،
// ومعاينةُ صنف. لا أثرَ لأيٍّ منها في الخادم، فلا مكان آخر تُلتقط منه.
//
// **مجهولةٌ عمداً**: الزائرُ غيرُ المسجَّل هو أكثرُ مَن يتصفّح، وقياسٌ لا يراه إلّا المسجَّلون
// يقيس الأقلّية. وهويّةُ المتجر تُحلّ من المضيف كبقيّة واجهة المتجر، ولا تُقبل من الجسم.
//
// **و202 دائماً ما دام الشكل صحيحاً**: المتصفّح لا ينتظر قياساً، ولا يجوز أن يظهر لمتسوّقٍ خطأٌ
// سببه قياس. ومتجرٌ لم يُفعّل الالتقاط يردّ 202 أيضاً بلا أن يكتب شيئاً — فلا يكشف الجوابُ
// إعدادَ المتجر لمن يستكشف.
//
// وما يُقبل من العميل معرّفاتٌ ومواضع وحدها؛ السعرُ وحالةُ التوفّر يقرؤهما الخادم من كتالوجه
// (انظر رأس `RecordStorefrontEvents`) — وبغير ذلك يستطيع أيُّ زائرٍ تسميم أرقام التاجر.
// ============================================================================
[ApiController]
[Route("api/storefront/events")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Events)]
public class StorefrontEventsController : ControllerBase
{
    private readonly IMediator _mediator;

    public StorefrontEventsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Record(RecordStorefrontEventsCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return Accepted();
    }
}
