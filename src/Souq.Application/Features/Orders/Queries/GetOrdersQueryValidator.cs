using FluentValidation;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Queries;

// قواعد الصفحة الموحّدة (PagedQueryValidator)، ولقائمة الإدارة مرشّحاتها (المرحلة 9): بحث قصير ونطاق تاريخ مرتّب.
public class GetOrdersQueryValidator : PagedQueryValidator<GetOrdersQuery>
{
    public GetOrdersQueryValidator()
    {
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status is not null);
        RuleFor(q => q.To).GreaterThan(q => q.From).When(q => q.From is not null && q.To is not null);
    }
}

public class GetMyOrdersQueryValidator : PagedQueryValidator<GetMyOrdersQuery>;
