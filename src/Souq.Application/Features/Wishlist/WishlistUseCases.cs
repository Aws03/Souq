using FluentValidation;
using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Wishlist;

// ============================================================================
// مفضّلة العميل على الخادم (المرحلة 13، وحدة Shopping، ADR-0033). العميل هو المستخدم الحالي دائماً — لا معرّف عميل في أي
// طلب — والمنتج من متجر السياق وحده: منتج متجر آخر أو غير منشور ⇒ 404. كل عملية تعيد المفضّلة كاملةً بأسعار الكتالوج الحيّة
// (نمط السلة). الزائر يحفظ قائمته في متصفّحه، وتُدمج هنا عند دخوله (merge): ما لا يُعرف في المتجر أو لا يتّسع يُتجاهل بصمت.
// ============================================================================

// الشكل نفسه الذي تعرضه بطاقة المنتج في الواجهة (id، translations، price…) كي تُعرض المفضّلة بالبطاقة ذاتها.
public sealed record WishlistText(string Name);
public sealed record WishlistItemDto(
    int Id, string Slug, string Name, IReadOnlyDictionary<string, WishlistText> Translations,
    decimal Price, decimal? CompareAtPrice, string Currency, int StockQuantity, string? ImageUrl, DateTime AddedAt);
public sealed record WishlistDto(IReadOnlyList<WishlistItemDto> Items);

public interface IWishlistQueries
{
    // المنتجات الظاهرة في المتجر وحدها (نشطة وفئتها مفعّلة): المؤرشف يختفي من العرض ويعود إن أُعيد نشره. الأحدث أولاً.
    Task<IReadOnlyList<WishlistItemDto>> ListAsync(int customerId, string culture, CancellationToken ct);
}

public record GetWishlistQuery : IRequest<WishlistDto>;
public record AddToWishlistCommand(int ProductId) : IRequest<Result<WishlistDto>>;
public record RemoveFromWishlistCommand(int ProductId) : IRequest<Result<WishlistDto>>;
public record MergeWishlistCommand(IReadOnlyList<int> ProductIds) : IRequest<Result<WishlistDto>>;

public sealed class MergeWishlistValidator : AbstractValidator<MergeWishlistCommand>
{
    public MergeWishlistValidator()
    {
        RuleFor(x => x.ProductIds).NotNull()
            .Must(ids => ids.Count <= WishlistItem.MaxItemsPerCustomer)
            .WithMessage($"حتى {WishlistItem.MaxItemsPerCustomer} منتج في الدمج الواحد");
        RuleForEach(x => x.ProductIds).GreaterThan(0);
    }
}

internal static class WishlistView
{
    public static async Task<WishlistDto> ForAsync(IWishlistQueries queries, ITenantContext tenant, int customerId, CancellationToken ct) =>
        new(await queries.ListAsync(customerId, tenant.RequireTenant().DefaultCulture, ct));
}

public class GetWishlistHandler : IRequestHandler<GetWishlistQuery, WishlistDto>
{
    private readonly IWishlistQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public GetWishlistHandler(IWishlistQueries queries, ITenantContext tenant, ICurrentUser currentUser)
    {
        _queries = queries; _tenant = tenant; _currentUser = currentUser;
    }

    public Task<WishlistDto> Handle(GetWishlistQuery query, CancellationToken ct) =>
        WishlistView.ForAsync(_queries, _tenant, _currentUser.RequireCustomerId(), ct);
}

// الإضافة متساوية الأثر: منتج في المفضّلة أصلاً ⇒ نجاح بلا تغيير (نقرتان على القلب لا تكرّران؛ القيد الفريد يحرس السباق).
public class AddToWishlistHandler : IRequestHandler<AddToWishlistCommand, Result<WishlistDto>>
{
    private readonly IWishlistRepository _wishlist;
    private readonly IProductRepository _products;
    private readonly IWishlistQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public AddToWishlistHandler(
        IWishlistRepository wishlist, IProductRepository products, IWishlistQueries queries, ITenantContext tenant,
        ICurrentUser currentUser, IUnitOfWork uow)
    {
        _wishlist = wishlist; _products = products; _queries = queries; _tenant = tenant; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<WishlistDto>> Handle(AddToWishlistCommand cmd, CancellationToken ct)
    {
        var customerId = _currentUser.RequireCustomerId();
        if (await _wishlist.FindAsync(customerId, cmd.ProductId, ct) is null)
        {
            var product = await _products.GetByIdAsync(cmd.ProductId, ct);
            if (product is not { IsSellable: true })
                return Result<WishlistDto>.Failure(Error.NotFound("المنتج غير موجود"));
            if (!WishlistItem.HasRoom(await _wishlist.CountForCustomerAsync(customerId, ct)))
                return Result<WishlistDto>.Failure(Error.BusinessRule("WishlistFull",
                    $"المفضّلة تتّسع لـ {WishlistItem.MaxItemsPerCustomer} منتج — احذف بعضها أولاً"));

            await _wishlist.AddAsync(new WishlistItem(customerId, cmd.ProductId), ct);
            await _uow.SaveChangesAsync(ct);
        }

        return Result<WishlistDto>.Success(await WishlistView.ForAsync(_queries, _tenant, customerId, ct));
    }
}

public class RemoveFromWishlistHandler : IRequestHandler<RemoveFromWishlistCommand, Result<WishlistDto>>
{
    private readonly IWishlistRepository _wishlist;
    private readonly IWishlistQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public RemoveFromWishlistHandler(
        IWishlistRepository wishlist, IWishlistQueries queries, ITenantContext tenant, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _wishlist = wishlist; _queries = queries; _tenant = tenant; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<WishlistDto>> Handle(RemoveFromWishlistCommand cmd, CancellationToken ct)
    {
        var customerId = _currentUser.RequireCustomerId();
        var item = await _wishlist.FindAsync(customerId, cmd.ProductId, ct);
        if (item is null)
            return Result<WishlistDto>.Failure(Error.NotFound("المنتج ليس في المفضّلة"));

        _wishlist.Remove(item);
        await _uow.SaveChangesAsync(ct);
        return Result<WishlistDto>.Success(await WishlistView.ForAsync(_queries, _tenant, customerId, ct));
    }
}

// دمج قائمة الزائر عند الدخول: ترتيبها محفوظ، والموجود لا يتكرّر، وما ليس منتجاً منشوراً في هذا المتجر أو يتجاوز السقف يُتجاهل
// — القائمة المحلية قد تحمل منتجاً أُرشف منذ حفظه، وليست سبباً لفشل الدخول.
public class MergeWishlistHandler : IRequestHandler<MergeWishlistCommand, Result<WishlistDto>>
{
    private readonly IWishlistRepository _wishlist;
    private readonly IProductRepository _products;
    private readonly IWishlistQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public MergeWishlistHandler(
        IWishlistRepository wishlist, IProductRepository products, IWishlistQueries queries, ITenantContext tenant,
        ICurrentUser currentUser, IUnitOfWork uow)
    {
        _wishlist = wishlist; _products = products; _queries = queries; _tenant = tenant; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<WishlistDto>> Handle(MergeWishlistCommand cmd, CancellationToken ct)
    {
        var customerId = _currentUser.RequireCustomerId();
        var existing = (await _wishlist.ListForCustomerAsync(customerId, ct)).Select(i => i.ProductId).ToHashSet();
        var candidates = cmd.ProductIds.Distinct().Where(id => !existing.Contains(id)).ToList();
        var room = WishlistItem.MaxItemsPerCustomer - existing.Count;

        if (candidates.Count > 0 && room > 0)
        {
            var published = (await _products.GetManyAsync(candidates, ct)).Where(p => p.IsSellable).Select(p => p.Id).ToHashSet();
            var added = 0;
            foreach (var productId in candidates.Where(published.Contains).Take(room))
            {
                await _wishlist.AddAsync(new WishlistItem(customerId, productId), ct);
                added++;
            }
            if (added > 0) await _uow.SaveChangesAsync(ct);
        }

        return Result<WishlistDto>.Success(await WishlistView.ForAsync(_queries, _tenant, customerId, ct));
    }
}
