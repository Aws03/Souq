using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Features.Analytics.Contracts;
using Souq.Application.Common.Models;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Baskets;

// ============================================================================
// حالات استخدام السلة (المرحلة 8، ADR-0028): عرض، إضافة، تعديل كمية، حذف سطر، تفريغ — لزائر أو عميل (BasketResolver).
// المنتج يُضاف منشوراً ومن هذا المتجر فقط (غيره 404 — المستودع مُرشَّح بالمتجر)، والكمية لا تتجاوز المتاح لحظتها
// (قراءة بلا حجز؛ الدفع وحده يحجز). كل استجابة هي السلة مسعَّرةً من خطّ التسعير الواحد (BasketViews ← IPricing).
// ============================================================================

// العرض يكتب في حالة واحدة فقط: دمج سلة زائر بعد الدخول (أو حذف سلة زائر منتهية).
// ShippingMethodId/Country (المرحلة 12): تسعير الدفع بطريقة شحن ودولة العنوان المختار (معاينة؛ الطلب يأخذ الدولة من
// دفتر العميل نفسه).
public record GetBasketQuery(string? GuestToken, string? CouponCode = null, int? ShippingMethodId = null, string? Country = null)
    : IRequest<Result<BasketResult>>;

public class GetBasketValidator : AbstractValidator<GetBasketQuery>
{
    public GetBasketValidator()
    {
        RuleFor(q => q.CouponCode).MaximumLength(50);
        RuleFor(q => q.ShippingMethodId).GreaterThan(0).When(q => q.ShippingMethodId is not null);
        RuleFor(q => q.Country).Matches("^[A-Za-z]{2}$").When(q => q.Country is not null)
            .WithMessage("الدولة برمز ISO من حرفين");
    }
}

public class GetBasketHandler : IRequestHandler<GetBasketQuery, Result<BasketResult>>
{
    private readonly BasketResolver _resolver;
    private readonly BasketViews _views;
    private readonly IUnitOfWork _uow;
    private readonly BasketWriter _writer;

    public GetBasketHandler(BasketResolver resolver, BasketViews views, IUnitOfWork uow, BasketWriter writer)
    {
        _resolver = resolver; _views = views; _uow = uow; _writer = writer;
    }

    // قراءةٌ تكتب: `ResolveAsync` يدمج سلّة الزائر ويحذفها. فتُعاد كاملةً عند التعارض (F-28) —
    // وليس الحفظ وحده، لأن القرار نفسه (أي سلّة، وهل يُدمج) يتغيّر بالحالة الملتزمة الجديدة.
    public Task<Result<BasketResult>> Handle(GetBasketQuery query, CancellationToken ct) =>
        _writer.SaveAsync(() => AttemptAsync(query, ct), ct);

    private async Task<Result<BasketResult>> AttemptAsync(GetBasketQuery query, CancellationToken ct)
    {
        var resolved = await _resolver.ResolveAsync(query.GuestToken, ct);
        await _uow.SaveChangesAsync(ct);
        var shipping = new Contracts.ShippingRequest(query.ShippingMethodId, query.Country?.ToUpperInvariant());
        return Result<BasketResult>.Success(resolved.Read(await _views.BuildAsync(resolved.Basket, query.CouponCode, shipping, ct)));
    }
}

// VariantId اختياري: بدونه يُضاف متغيّر المنتج الضمني (الوحيد النشط — كل منتج اليوم)، ولمنتج بأكثر من متغيّر نشط يُرفض
// بـ VariantRequired بدل افتراض الافتراضي بصمت (P-08c). معه: متغيّر نشط من هذا المنتج نفسه وإلا 404 — معرّف متغيّر
// منتج آخر أو متجر آخر "غير موجود" هنا، فلا يُسعَّر منتج بمتغيّر غيره.
public record AddBasketItemCommand(string? GuestToken, int ProductId, int Quantity = 1, int? VariantId = null)
    : IRequest<Result<BasketResult>>;

public class AddBasketItemValidator : AbstractValidator<AddBasketItemCommand>
{
    public AddBasketItemValidator()
    {
        RuleFor(c => c.ProductId).GreaterThan(0);
        RuleFor(c => c.VariantId).GreaterThan(0).When(c => c.VariantId is not null);
        RuleFor(c => c.Quantity).InclusiveBetween(1, Basket.MaxQuantityPerLine);
    }
}

public class AddBasketItemHandler : IRequestHandler<AddBasketItemCommand, Result<BasketResult>>
{
    private readonly IProductRepository _products;
    private readonly IStockAvailability _availability;
    private readonly BasketResolver _resolver;
    private readonly BasketViews _views;
    private readonly IUnitOfWork _uow;

    private readonly BasketWriter _writer;
    private readonly IEventSink _events;
    private readonly ILogger<AddBasketItemHandler> _logger;

    public AddBasketItemHandler(
        IProductRepository products, IStockAvailability availability, BasketResolver resolver, BasketViews views,
        IUnitOfWork uow, BasketWriter writer, IEventSink events, ILogger<AddBasketItemHandler> logger)
    {
        _products = products; _availability = availability; _resolver = resolver; _views = views; _uow = uow;
        _writer = writer; _events = events; _logger = logger;
    }

    public Task<Result<BasketResult>> Handle(AddBasketItemCommand cmd, CancellationToken ct) =>
        _writer.SaveAsync(() => AttemptAsync(cmd, ct), ct);

    private async Task<Result<BasketResult>> AttemptAsync(AddBasketItemCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is not { IsSellable: true })
            return Result<BasketResult>.Failure(Error.NotFound("المنتج غير متاح"));

        ProductVariant variant;
        if (cmd.VariantId is int requested)
        {
            if (product.FindVariant(requested) is not { } found || !product.CanSell(found))
                return Result<BasketResult>.Failure(Error.NotFound("المتغيّر غير متاح"));
            variant = found;
        }
        else if (product.ImplicitVariant is { } implicitVariant)
            variant = implicitVariant;
        else
            return Result<BasketResult>.Failure(BasketLines.VariantRequired());

        var resolved = await _resolver.ResolveAsync(cmd.GuestToken, ct);
        var quantity = (resolved.Basket?.LineForVariant(variant.Id)?.Quantity ?? 0) + cmd.Quantity;
        if (await BasketStock.ShortageAsync(_availability, variant.Id, quantity, ct) is { } shortage)
            return Result<BasketResult>.Failure(shortage);

        resolved = await _resolver.EnsureAsync(resolved, ct);
        var basket = resolved.Basket!;
        basket.Add(product.Id, variant.Id, cmd.Quantity, _resolver.ExpiryFor(basket));
        await _uow.SaveChangesAsync(ct);

        // ====================================================================
        // الإضافةُ إلى السلّة، بعد نجاح الحفظ لا قبله (C9، ADR-0050 §3).
        //
        // **والسعرُ يُجمَّد هنا** لا يُقرأ لاحقاً: سعرُ المنتج يتغيّر، والسؤالُ الذي يُجيب عنه
        // هذا الحدث هو «بكم أُضيف يومها». وقراءتُه من المنتج بعد شهرٍ تُجيب سؤالاً آخر.
        //
        // ومعرّفُ تنفيذ البحث لا يُمرَّر: `EventBuffer` يختمه من سياق الطلب، فالسلسلةُ
        // بحث ← إضافة ← شراء تُقفل بلا أن يحملها كلُّ مُنادٍ بيده.
        //
        // والحراسةُ للسبب نفسه الذي في `GetProductsHandler`: **سلّةُ المتسوّق لا تفشل بسبب قياس.**
        // ====================================================================
        if (_events.Enabled)
        {
            try
            {
                _events.Record(BehaviouralEventNames.CartAdded, new CartChangedPayload(
                    product.Id, variant.Id, cmd.Quantity, variant.Price.Amount, variant.Price.Currency));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Behavioural cart.added event rejected; the basket is unaffected");
            }
        }

        return Result<BasketResult>.Success(resolved.Written(await _views.BuildAsync(basket, null, ct)));
    }
}

// ── تعديل سطر وحذفه: بالمنتج (العقد الأصلي، صالح ما دام للمنتج سطر واحد في السلة) أو بالمتغيّر (السطر نفسه) ──

// صفر يحذف السطر. الزيادة وحدها تُقاس بالمتاح؛ الإنقاص مسموح دائماً.
public record SetBasketItemQuantityCommand(string? GuestToken, int ProductId, int Quantity) : IRequest<Result<BasketResult>>;

public class SetBasketItemQuantityValidator : AbstractValidator<SetBasketItemQuantityCommand>
{
    public SetBasketItemQuantityValidator()
    {
        RuleFor(c => c.ProductId).GreaterThan(0);
        RuleFor(c => c.Quantity).InclusiveBetween(0, Basket.MaxQuantityPerLine);
    }
}

public class SetBasketItemQuantityHandler : IRequestHandler<SetBasketItemQuantityCommand, Result<BasketResult>>
{
    private readonly BasketLines _lines;
    public SetBasketItemQuantityHandler(BasketLines lines) => _lines = lines;

    public Task<Result<BasketResult>> Handle(SetBasketItemQuantityCommand cmd, CancellationToken ct) =>
        _lines.SetQuantityAsync(cmd.GuestToken, basket => BasketLines.ByProduct(basket, cmd.ProductId), cmd.Quantity, ct);
}

public record SetBasketLineQuantityCommand(string? GuestToken, int VariantId, int Quantity) : IRequest<Result<BasketResult>>;

public class SetBasketLineQuantityValidator : AbstractValidator<SetBasketLineQuantityCommand>
{
    public SetBasketLineQuantityValidator()
    {
        RuleFor(c => c.VariantId).GreaterThan(0);
        RuleFor(c => c.Quantity).InclusiveBetween(0, Basket.MaxQuantityPerLine);
    }
}

public class SetBasketLineQuantityHandler : IRequestHandler<SetBasketLineQuantityCommand, Result<BasketResult>>
{
    private readonly BasketLines _lines;
    public SetBasketLineQuantityHandler(BasketLines lines) => _lines = lines;

    public Task<Result<BasketResult>> Handle(SetBasketLineQuantityCommand cmd, CancellationToken ct) =>
        _lines.SetQuantityAsync(cmd.GuestToken, basket => BasketLines.ByVariant(basket, cmd.VariantId), cmd.Quantity, ct);
}

public record RemoveBasketItemCommand(string? GuestToken, int ProductId) : IRequest<Result<BasketResult>>;

public class RemoveBasketItemHandler : IRequestHandler<RemoveBasketItemCommand, Result<BasketResult>>
{
    private readonly BasketLines _lines;
    public RemoveBasketItemHandler(BasketLines lines) => _lines = lines;

    public Task<Result<BasketResult>> Handle(RemoveBasketItemCommand cmd, CancellationToken ct) =>
        _lines.RemoveAsync(cmd.GuestToken, basket => BasketLines.ByProduct(basket, cmd.ProductId), ct);
}

public record RemoveBasketLineCommand(string? GuestToken, int VariantId) : IRequest<Result<BasketResult>>;

public class RemoveBasketLineHandler : IRequestHandler<RemoveBasketLineCommand, Result<BasketResult>>
{
    private readonly BasketLines _lines;
    public RemoveBasketLineHandler(BasketLines lines) => _lines = lines;

    public Task<Result<BasketResult>> Handle(RemoveBasketLineCommand cmd, CancellationToken ct) =>
        _lines.RemoveAsync(cmd.GuestToken, basket => BasketLines.ByVariant(basket, cmd.VariantId), ct);
}

// ما يشترك فيه تعديل السطر بالمنتج وبالمتغيّر بعد تحديد السطر — قواعد الكمية نفسها في Basket، لا نسخة ثانية منها.
public sealed class BasketLines
{
    private readonly IStockAvailability _availability;
    private readonly BasketResolver _resolver;
    private readonly BasketViews _views;
    private readonly IUnitOfWork _uow;

    private readonly BasketWriter _writer;
    private readonly IProductRepository _products;
    private readonly IEventSink _events;
    private readonly ILogger<BasketLines> _logger;

    public BasketLines(
        IStockAvailability availability, BasketResolver resolver, BasketViews views, IUnitOfWork uow,
        BasketWriter writer, IProductRepository products, IEventSink events, ILogger<BasketLines> logger)
    {
        _availability = availability; _resolver = resolver; _views = views; _uow = uow; _writer = writer;
        _products = products; _events = events; _logger = logger;
    }

    public static Error VariantRequired() =>
        Error.BusinessRule("VariantRequired", "لهذا المنتج أكثر من متغيّر: حدّد المتغيّر المطلوب");

    // بالمنتج: سطره الوحيد. سطران لمتغيّرين من المنتج نفسه ⇒ المسار ملتبس، فيُطلب التحديد بدل تعديل سطر لم يقصده العميل.
    public static Result<BasketLine> ByProduct(Basket? basket, int productId) =>
        basket?.LinesFor(productId) switch
        {
            null or { Count: 0 } => Result<BasketLine>.Failure(Error.NotFound("الصنف ليس في السلة")),
            { Count: 1 } lines => Result<BasketLine>.Success(lines[0]),
            _ => Result<BasketLine>.Failure(VariantRequired()),
        };

    public static Result<BasketLine> ByVariant(Basket? basket, int variantId) =>
        basket?.LineForVariant(variantId) is { } line
            ? Result<BasketLine>.Success(line)
            : Result<BasketLine>.Failure(Error.NotFound("الصنف ليس في السلة"));

    public Task<Result<BasketResult>> SetQuantityAsync(
        string? guestToken, Func<Basket?, Result<BasketLine>> find, int quantity, CancellationToken ct) =>
        _writer.SaveAsync(() => SetQuantityAttemptAsync(guestToken, find, quantity, ct), ct);

    private async Task<Result<BasketResult>> SetQuantityAttemptAsync(
        string? guestToken, Func<Basket?, Result<BasketLine>> find, int quantity, CancellationToken ct)
    {
        var resolved = await _resolver.ResolveAsync(guestToken, ct);
        var basket = resolved.Basket;
        var found = find(basket);
        if (!found.IsSuccess) return Result<BasketResult>.Failure(found.Error!);
        var line = found.Value!;

        if (quantity > line.Quantity
            && await BasketStock.ShortageAsync(_availability, line.VariantId, quantity, ct) is { } shortage)
            return Result<BasketResult>.Failure(shortage);

        basket!.SetQuantity(line.VariantId, quantity, _resolver.ExpiryFor(basket));
        await _uow.SaveChangesAsync(ct);
        return Result<BasketResult>.Success(resolved.Written(await _views.BuildAsync(basket, null, ct)));
    }

    public Task<Result<BasketResult>> RemoveAsync(string? guestToken, Func<Basket?, Result<BasketLine>> find, CancellationToken ct) =>
        _writer.SaveAsync(() => RemoveAttemptAsync(guestToken, find, ct), ct);

    private async Task<Result<BasketResult>> RemoveAttemptAsync(string? guestToken, Func<Basket?, Result<BasketLine>> find, CancellationToken ct)
    {
        var resolved = await _resolver.ResolveAsync(guestToken, ct);
        var basket = resolved.Basket;
        var found = find(basket);
        if (!found.IsSuccess) return Result<BasketResult>.Failure(found.Error!);

        var line = found.Value!;
        basket!.Remove(line.VariantId, _resolver.ExpiryFor(basket));
        await _uow.SaveChangesAsync(ct);

        // ====================================================================
        // الحذفُ من السلّة (C9، ADR-0050 §3): ما تُرك يُقاس كما يُقاس ما أُخذ — «أُضيف ثمّ حُذف»
        // إشارةٌ عن السعر أو المخزون لا تُستخرج من الطلبات، ولا تُستردّ لاحقاً إن لم تُلتقط.
        //
        // **وقراءةُ المنتج تقع داخل الحارس عمداً**: السطرُ لا يحمل سعراً (السلّة تُسعَّر حيّةً)،
        // فالسعرُ يحتاج قراءةً — ومسارُ الحذف لا يدفع ثمنها ما دام الالتقاط معطّلاً.
        // ====================================================================
        if (_events.Enabled)
        {
            try
            {
                var product = await _products.GetByIdAsync(line.ProductId, ct);
                if (product?.FindVariant(line.VariantId)?.Price is { } price)
                {
                    _events.Record(BehaviouralEventNames.CartRemoved, new CartChangedPayload(
                        line.ProductId, line.VariantId, line.Quantity, price.Amount, price.Currency));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Behavioural cart.removed event rejected; the basket is unaffected");
            }
        }

        return Result<BasketResult>.Success(resolved.Written(await _views.BuildAsync(basket, null, ct)));
    }
}

public record ClearBasketCommand(string? GuestToken) : IRequest<Result<BasketResult>>;

public class ClearBasketHandler : IRequestHandler<ClearBasketCommand, Result<BasketResult>>
{
    private readonly BasketResolver _resolver;
    private readonly BasketViews _views;
    private readonly IUnitOfWork _uow;

    private readonly BasketWriter _writer;

    public ClearBasketHandler(BasketResolver resolver, BasketViews views, IUnitOfWork uow, BasketWriter writer)
    {
        _resolver = resolver; _views = views; _uow = uow; _writer = writer;
    }

    public Task<Result<BasketResult>> Handle(ClearBasketCommand cmd, CancellationToken ct) =>
        _writer.SaveAsync(() => AttemptAsync(cmd, ct), ct);

    private async Task<Result<BasketResult>> AttemptAsync(ClearBasketCommand cmd, CancellationToken ct)
    {
        var resolved = await _resolver.ResolveAsync(cmd.GuestToken, ct);
        resolved.Basket?.Clear(_resolver.ExpiryFor(resolved.Basket));
        await _uow.SaveChangesAsync(ct);

        var view = await _views.BuildAsync(resolved.Basket, null, ct);
        return Result<BasketResult>.Success(resolved.Basket is null ? resolved.Read(view) : resolved.Written(view));
    }
}

// حذف السلال المنتهية لمتجر السياق (منسّق Infrastructure الدوري، المرحلة 8) — دفعة محدودة كل دورة كي تبقى المعاملة صغيرة.
public record PurgeExpiredBasketsCommand(int Max = 500) : IRequest<int>;

public class PurgeExpiredBasketsHandler : IRequestHandler<PurgeExpiredBasketsCommand, int>
{
    private readonly IBasketRepository _baskets;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public PurgeExpiredBasketsHandler(IBasketRepository baskets, IUnitOfWork uow, TimeProvider clock)
    {
        _baskets = baskets; _uow = uow; _clock = clock;
    }

    public async Task<int> Handle(PurgeExpiredBasketsCommand cmd, CancellationToken ct)
    {
        var expired = await _baskets.ListExpiredAsync(_clock.GetUtcNow().UtcDateTime, cmd.Max, ct);
        foreach (var basket in expired) _baskets.Remove(basket);
        await _uow.SaveChangesAsync(ct);
        return expired.Count;
    }
}

// قراءة مبكرة للمتاح لرسالة واضحة — ليست حجزاً؛ الدفع يعيد التحقّق ويحجز.
internal static class BasketStock
{
    public static async Task<Error?> ShortageAsync(IStockAvailability availability, int variantId, int requested, CancellationToken ct)
    {
        var inStock = (await availability.AvailableAsync([variantId], ct)).GetValueOrDefault(variantId);
        return requested > inStock
            ? Error.BusinessRule("InsufficientStock", $"الكمية المطلوبة ({requested}) غير متوفرة. المتاح: {Math.Max(inStock, 0)}")
            : null;
    }
}
