using FluentValidation;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// تحقّق شكلي وحده (400 برسالة حقل) — القاعدة الحقيقية في المجال (SearchSynonym): كلمة واحدة لكل طرف، لغة
// مدعومة، ولا كلمة إلى نفسها. لا تُكرَّر هنا كي لا يفترق التنفيذان؛ هذه الطبقة تمنع الفارغ والطويل فقط،
// فيرى التاجر خطأ حقلٍ بدل 422 عامّ.
// ============================================================================
public class CreateSearchSynonymValidator : AbstractValidator<CreateSearchSynonymCommand>
{
    public CreateSearchSynonymValidator()
    {
        RuleFor(x => x.Culture).NotEmpty();
        RuleFor(x => x.Term).NotEmpty().MaximumLength(SearchSynonym.TermMaxLength);
        RuleFor(x => x.Expansion).NotEmpty().MaximumLength(SearchSynonym.TermMaxLength);
    }
}

public class UpdateSearchSynonymValidator : AbstractValidator<UpdateSearchSynonymCommand>
{
    public UpdateSearchSynonymValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Culture).NotEmpty();
        RuleFor(x => x.Term).NotEmpty().MaximumLength(SearchSynonym.TermMaxLength);
        RuleFor(x => x.Expansion).NotEmpty().MaximumLength(SearchSynonym.TermMaxLength);
    }
}

public class DeleteSearchSynonymValidator : AbstractValidator<DeleteSearchSynonymCommand>
{
    public DeleteSearchSynonymValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
