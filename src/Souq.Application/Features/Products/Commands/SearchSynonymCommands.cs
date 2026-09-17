using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Auditing;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// مفردات بحث المتجر (M3، ADR-0042): التاجر يُعلِّم محرّك البحث كلمات زبائنه التي لا ترد في كتالوجه — لهجة
// محلية، اسم تجاري شائع، أو تسمية أخرى للشيء نفسه ("جوال" ⇒ "هاتف").
//
// **ما لا يُحلّه التصحيح الآلي:** استرجاع الخطأ المطبعي يعمل على مسافة تحرير من كلمات الكتالوج نفسه، فلا يصل
// إلى كلمة لا تشبه أي كلمة فيه. هذه الحالة بعينها هي ما تملؤه المفردات: قرار لغوي يعرفه التاجر ولا تعرفه
// المسافة، ويبقى مكتوباً وقابلاً للحذف — لا مخفياً في نموذج.
//
// مُدقَّقة (IAuditable) كأوامر الفئات: المفردات تغيّر ما يجده الزبائن، فيُعرَف من غيّرها ومتى.
// ============================================================================
public record CreateSearchSynonymCommand(string? Culture, string? Term, string? Expansion)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.search-synonym.created", "SearchSynonym",
        Term?.Trim(), Metadata: new Dictionary<string, object?> { ["culture"] = Culture, ["expansion"] = Expansion });
}

public record UpdateSearchSynonymCommand(int Id, string? Culture, string? Term, string? Expansion)
    : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.search-synonym.updated", "SearchSynonym",
        Id.ToString(), Metadata: new Dictionary<string, object?> { ["term"] = Term, ["expansion"] = Expansion });
}

public record DeleteSearchSynonymCommand(int Id) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.search-synonym.deleted", "SearchSynonym", Id.ToString());
}

internal static class SearchSynonymRules
{
    public static Error Duplicate => Error.Conflict("SearchSynonymExists", "هذا الزوج مُضاف مسبقاً لهذه اللغة");
    public static Error NotFound => Error.NotFound("المرادف غير موجود");
    public static Error LimitReached => Error.Validation("SearchSynonymLimitReached",
        $"الحدّ الأقصى للمرادفات في المتجر {SearchSynonym.MaxPerStore}");
}

public class CreateSearchSynonymHandler : IRequestHandler<CreateSearchSynonymCommand, Result<int>>
{
    private readonly ISearchSynonymRepository _synonyms;
    private readonly IUnitOfWork _uow;

    public CreateSearchSynonymHandler(ISearchSynonymRepository synonyms, IUnitOfWork uow)
    {
        _synonyms = synonyms; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateSearchSynonymCommand cmd, CancellationToken ct)
    {
        if (await _synonyms.CountAsync(ct) >= SearchSynonym.MaxPerStore)
            return Result<int>.Failure(SearchSynonymRules.LimitReached);

        // يُبنى أولاً كي يطبّع المجال الكلمتين ويتحقّق منهما — التكرار يُقاس على الصورة المطبَّعة لا على ما كُتب.
        var synonym = new SearchSynonym(cmd.Culture, cmd.Term, cmd.Expansion);
        if (await _synonyms.ExistsAsync(synonym.Culture, synonym.TermNormalized, synonym.ExpansionNormalized, null, ct))
            return Result<int>.Failure(SearchSynonymRules.Duplicate);

        await _synonyms.AddAsync(synonym, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(synonym.Id);
    }
}

public class UpdateSearchSynonymHandler : IRequestHandler<UpdateSearchSynonymCommand, Result>
{
    private readonly ISearchSynonymRepository _synonyms;
    private readonly IUnitOfWork _uow;

    public UpdateSearchSynonymHandler(ISearchSynonymRepository synonyms, IUnitOfWork uow)
    {
        _synonyms = synonyms; _uow = uow;
    }

    public async Task<Result> Handle(UpdateSearchSynonymCommand cmd, CancellationToken ct)
    {
        // صفّ متجر آخر "غير موجود" (لا 403): المرشّح العام لا يراه أصلاً.
        var synonym = await _synonyms.GetByIdAsync(cmd.Id, ct);
        if (synonym is null) return Result.Failure(SearchSynonymRules.NotFound);

        var replacement = new SearchSynonym(cmd.Culture, cmd.Term, cmd.Expansion);
        if (await _synonyms.ExistsAsync(replacement.Culture, replacement.TermNormalized,
                replacement.ExpansionNormalized, cmd.Id, ct))
            return Result.Failure(SearchSynonymRules.Duplicate);

        synonym.Update(cmd.Culture, cmd.Term, cmd.Expansion);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class DeleteSearchSynonymHandler : IRequestHandler<DeleteSearchSynonymCommand, Result>
{
    private readonly ISearchSynonymRepository _synonyms;
    private readonly IUnitOfWork _uow;

    public DeleteSearchSynonymHandler(ISearchSynonymRepository synonyms, IUnitOfWork uow)
    {
        _synonyms = synonyms; _uow = uow;
    }

    public async Task<Result> Handle(DeleteSearchSynonymCommand cmd, CancellationToken ct)
    {
        var synonym = await _synonyms.GetByIdAsync(cmd.Id, ct);
        if (synonym is null) return Result.Failure(SearchSynonymRules.NotFound);

        _synonyms.Remove(synonym);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
