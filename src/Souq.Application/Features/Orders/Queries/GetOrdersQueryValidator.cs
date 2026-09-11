using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Queries;

// قواعد الصفحة الموحّدة فقط (PagedQueryValidator) — لا معايير أخرى لهاتين القائمتين بعد.
public class GetOrdersQueryValidator : PagedQueryValidator<GetOrdersQuery>;

public class GetMyOrdersQueryValidator : PagedQueryValidator<GetMyOrdersQuery>;
