using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Categories.Commands;

// حذف فعلي للفئة (لا حذف منطقي — الفئة لا تحمل تاريخاً ماليّاً كالمنتج). محروس:
// لا تُحذف فئة تحتوي منتجات أو لها فئات فرعية (يحمي التكامل المرجعي بوضوح
// بدل ترك قاعدة البيانات ترمي خطأ مفتاح أجنبي غامضاً).
public record DeleteCategoryCommand(int Id) : IRequest<Result>;

public class DeleteCategoryHandler : IRequestHandler<DeleteCategoryCommand, Result>
{
    private readonly ICategoryRepository _categories;
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public DeleteCategoryHandler(
        ICategoryRepository categories, IProductRepository products, IUnitOfWork uow)
    {
        _categories = categories; _products = products; _uow = uow;
    }

    public async Task<Result> Handle(DeleteCategoryCommand cmd, CancellationToken ct)
    {
        var category = await _categories.GetByIdAsync(cmd.Id, ct);
        if (category is null)
            return Result.Failure("الفئة غير موجودة", "NotFound");

        if (await _products.ExistsInCategoryAsync(cmd.Id, ct))
            return Result.Failure("لا يمكن حذف فئة تحتوي منتجات", "CategoryInUse");

        if (await _categories.HasChildrenAsync(cmd.Id, ct))
            return Result.Failure("لا يمكن حذف فئة لها فئات فرعية", "CategoryHasChildren");

        _categories.Remove(category);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
