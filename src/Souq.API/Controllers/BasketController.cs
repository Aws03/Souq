using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Models;
using Souq.Application.Features.Baskets;

namespace Souq.API.Controllers;

// ============================================================================
// السلة (المرحلة 8، /api/basket، ADR-0028): للزائر والعميل معاً. الزائر يُعرَّف برمز عشوائي في ملف تعريف ارتباط HttpOnly
// (SameSite=Strict، مقصور على /api/basket — لا يقرؤه JavaScript ولا يُرسَل لغير هذه النقاط)، والعميل بجلسته؛ أول طلب
// سلة بعد دخوله يدمج سلة الزائر ويمسح الرمز. التسعير بكوبون في /quote خلف حدّ معدّل معاينة الكوبونات (تخمين الرموز)،
// والكتابة خلف حدّ خاص (كل إضافة من زائر جديد تُنشئ سلة).
// ============================================================================
[ApiController]
[Route("api/basket")]
[AllowAnonymous]
public class BasketController : ControllerBase
{
    // نفس حرس ملفّ التجديد (M15، `HostOnlyCookie`): مضيفٌ شقيق تحت نطاق التاجر كان يستطيع زرع رمز
    // سلّته هنا، فتذهب مشتريات الزائر إلى سلّةٍ يقرؤها هو. أخفّ من الاستيلاء على حساب وأثقل من لا شيء.
    public const string GuestCookieBareName = "souq_basket";

    private string GuestCookieName => HostOnlyCookie.NameFor(GuestCookieBareName, _cookie.Secure);
    private const string GuestCookiePath = "/api/basket";

    private readonly IMediator _mediator;
    private readonly RefreshCookieOptions _cookie;

    public BasketController(IMediator mediator, IOptions<RefreshCookieOptions> cookie)
    {
        _mediator = mediator;
        _cookie = cookie.Value;
    }

    private string? GuestToken => Request.Cookies[GuestCookieName];

    [HttpGet]
    public async Task<IActionResult> Get() => Respond(await _mediator.Send(new GetBasketQuery(GuestToken)));

    // GET /api/basket/quote?couponCode=SAVE10&shippingMethodId=2&country=JO — السلة مسعَّرةً بكوبون وطريقة شحن لدولة
    // العنوان، بالخطّ نفسه الذي يُنشئ الطلب (المرحلة 12).
    [HttpGet("quote")]
    [EnableRateLimiting(RateLimitPolicies.CouponPreview)]
    public async Task<IActionResult> Quote(
        [FromQuery] string? couponCode, [FromQuery] int? shippingMethodId, [FromQuery] string? country) =>
        Respond(await _mediator.Send(new GetBasketQuery(GuestToken, couponCode, shippingMethodId, country)));

    // POST /api/basket/items { "productId": 5, "variantId": 12, "quantity": 1 } — variantId اختياري لمنتج بمتغيّر نشط واحد
    // (VariantRequired لغيره).
    [HttpPost("items")]
    [EnableRateLimiting(RateLimitPolicies.Basket)]
    public async Task<IActionResult> Add([FromBody] BasketItemRequest body) =>
        Respond(await _mediator.Send(new AddBasketItemCommand(GuestToken, body.ProductId, body.Quantity, body.VariantId)));

    // PUT /api/basket/items/5 { "quantity": 3 } — صفر يحذف السطر. بالمنتج: صالح ما دام للمنتج سطر واحد في السلة.
    [HttpPut("items/{productId:int}")]
    [EnableRateLimiting(RateLimitPolicies.Basket)]
    public async Task<IActionResult> SetQuantity(int productId, [FromBody] BasketQuantityRequest body) =>
        Respond(await _mediator.Send(new SetBasketItemQuantityCommand(GuestToken, productId, body.Quantity)));

    [HttpDelete("items/{productId:int}")]
    [EnableRateLimiting(RateLimitPolicies.Basket)]
    public async Task<IActionResult> Remove(int productId) =>
        Respond(await _mediator.Send(new RemoveBasketItemCommand(GuestToken, productId)));

    // PUT /api/basket/items/variants/12 { "quantity": 3 } — السطر بمتغيّره: الصيغة التي لا تلتبس حين يحمل المنتج أكثر من سطر.
    [HttpPut("items/variants/{variantId:int}")]
    [EnableRateLimiting(RateLimitPolicies.Basket)]
    public async Task<IActionResult> SetLineQuantity(int variantId, [FromBody] BasketQuantityRequest body) =>
        Respond(await _mediator.Send(new SetBasketLineQuantityCommand(GuestToken, variantId, body.Quantity)));

    [HttpDelete("items/variants/{variantId:int}")]
    [EnableRateLimiting(RateLimitPolicies.Basket)]
    public async Task<IActionResult> RemoveLine(int variantId) =>
        Respond(await _mediator.Send(new RemoveBasketLineCommand(GuestToken, variantId)));

    [HttpDelete]
    [EnableRateLimiting(RateLimitPolicies.Basket)]
    public async Task<IActionResult> Clear()
    {
        var result = await _mediator.Send(new ClearBasketCommand(GuestToken));
        if (!result.IsSuccess) return this.Failure(result);
        ApplyCookie(result.Value!);
        return NoContent();
    }

    private IActionResult Respond(Result<BasketResult> result)
    {
        if (!result.IsSuccess) return this.Failure(result);
        ApplyCookie(result.Value!);
        return Ok(result.Value!.Basket);
    }

    private void ApplyCookie(BasketResult result)
    {
        switch (result.Cookie)
        {
            case GuestCookieAction.Set:
                Response.Cookies.Append(GuestCookieName, result.GuestToken!, CookieOptions(result.GuestExpiresAt));
                break;
            case GuestCookieAction.Clear:
                Response.Cookies.Delete(GuestCookieName, CookieOptions(expires: null));
                break;
        }
    }

    // Secure من إعداد ملف تعريف التجديد نفسه: القيد واحد (نشر محلي على http بعنوان غير localhost فقط).
    private CookieOptions CookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = _cookie.Secure,
        SameSite = SameSiteMode.Strict,
        Path = HostOnlyCookie.PathFor(GuestCookiePath, _cookie.Secure),
        Expires = expires,
        IsEssential = true,   // لازم لوظيفة السلة، لا تتبّع
    };
}

public record BasketItemRequest(int ProductId, int Quantity = 1, int? VariantId = null);

public record BasketQuantityRequest(int Quantity);
