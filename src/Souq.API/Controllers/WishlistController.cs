using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Tenancy;
using Souq.Application.Features.Wishlist;
using Souq.Domain.Platform;

namespace Souq.API.Controllers;

// ============================================================================
// مفضّلة العميل (المرحلة 13، وحدة wishlist الاختيارية): معطّلة للمتجر ⇒ 404 ModuleDisabled. للعميل المسجّل فقط — الزائر يحفظ
// قائمته في متصفّحه ويدمجها هنا عند الدخول. كل عملية تعيد المفضّلة كاملةً. لا معرّف عميل في أي مسار: العميل هو المتصل.
// ============================================================================
[ApiController]
[Route("api/wishlist")]
[Authorize]
[RequiresModule(StoreModules.Wishlist)]
public class WishlistController : ControllerBase
{
    private readonly IMediator _mediator;
    public WishlistController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _mediator.Send(new GetWishlistQuery()));

    // PUT لا POST: الإضافة متساوية الأثر — تكرارها لا يكرّر المنتج.
    [HttpPut("{productId:int}")]
    public async Task<IActionResult> Add(int productId)
    {
        var result = await _mediator.Send(new AddToWishlistCommand(productId));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    [HttpDelete("{productId:int}")]
    public async Task<IActionResult> Remove(int productId)
    {
        var result = await _mediator.Send(new RemoveFromWishlistCommand(productId));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // { "productIds": [3, 7] } — قائمة الزائر المحلية بعد الدخول.
    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] MergeWishlistRequest body)
    {
        var result = await _mediator.Send(new MergeWishlistCommand(body.ProductIds ?? []));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }
}

public record MergeWishlistRequest(IReadOnlyList<int>? ProductIds);
