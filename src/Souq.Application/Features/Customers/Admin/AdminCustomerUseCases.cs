using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Customers.Admin;

// ============================================================================
// عملاء المتجر للإدارة (المرحلة 7): القائمة والتفاصيل بصلاحية customers.view، والحظر والتصدير والمحو بـ customers.manage
// (مُدقَّقة). المستودع والإسقاطات مُرشَّحة بالمتجر: معرّف عميل متجر آخر ⇒ 404.
// ============================================================================

// ============================================================================
// قراءة دفتر العملاء مُدقَّقة كما تصديرُه (M15، ASVS 8.3.5).
//
// كان التصدير وحده مُدقَّقاً — وهذا أسوأ من ألّا يكون شيءٌ مُدقَّقاً، لأنّ الأثر يقول "لم يُصدَّر شيء"
// بينما يخرج البيان نفسه من القائمة. و`CustomerListItemDto` يحمل الاسم **والبريد والهاتف** لكل عميل،
// مئةً في الصفحة: فمن يتصفّح الدفتر إلى آخره يحصد ما يحصده المُصدِّر تماماً.
//
// والثمن مقبول: تصفّح دفتر العملاء فعلُ تاجرٍ يجري مرّاتٍ في اليوم، لا مرّاتٍ في الثانية. أمّا نموّ
// جدول التدقيق بلا سياسة حفظ فثغرةٌ قائمة أصلاً لكل عملية مُدقَّقة (G-21)، لا أثرٌ لهذا السطر.
// ============================================================================
public record ListCustomersQuery(string? Keyword = null, CustomerStatus? Status = null, int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<CustomerListItemDto>>, IPagedQuery, IAuditable
{
    // المعايير في البيانات الوصفية: "من قرأ الدفتر" وحدها لا تكفي تحقيقاً — المهمّ أيُّ شريحةٍ قرأ.
    public AuditRecord ToAuditRecord() => new("customer.list-viewed", "Customer", null, null,
        new Dictionary<string, object?> { ["keyword"] = Keyword, ["status"] = Status?.ToString(), ["page"] = Page });
}

public class ListCustomersQueryValidator : PagedQueryValidator<ListCustomersQuery>
{
    public ListCustomersQueryValidator()
    {
        RuleFor(x => x.Keyword).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status is not null);
    }
}

public class ListCustomersHandler : IRequestHandler<ListCustomersQuery, PaginatedList<CustomerListItemDto>>
{
    private readonly ICustomerQueries _queries;
    public ListCustomersHandler(ICustomerQueries queries) => _queries = queries;

    public Task<PaginatedList<CustomerListItemDto>> Handle(ListCustomersQuery q, CancellationToken ct) =>
        _queries.ListAsync(new CustomerSearch(q.Keyword?.Trim(), q.Status), PageRequest.From(q), ct);
}

// فتح ملفّ عميلٍ بعينه: بيانه الكامل وعناوينه. مُدقَّق لنفس سبب التصدير.
public record GetCustomerQuery(int Id) : IRequest<Result<CustomerDetailDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("customer.profile-viewed", "Customer", Id.ToString());
}

public class GetCustomerHandler : IRequestHandler<GetCustomerQuery, Result<CustomerDetailDto>>
{
    private readonly ICustomerQueries _queries;
    public GetCustomerHandler(ICustomerQueries queries) => _queries = queries;

    public async Task<Result<CustomerDetailDto>> Handle(GetCustomerQuery q, CancellationToken ct) =>
        await _queries.FindDetailAsync(q.Id, ct) is { } detail
            ? Result<CustomerDetailDto>.Success(detail)
            : Result<CustomerDetailDto>.Failure(Error.NotFound("العميل غير موجود"));
}

// حظر عميل عن الشراء والتقييم أو رفعه (الدخول ورؤية طلباته وتصدير بياناته تبقى متاحة له).
public record SetCustomerStatusCommand(int Id, CustomerStatus Status) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("customer.status-changed", "Customer", Id.ToString(),
        Metadata: new Dictionary<string, object?> { ["status"] = Status.ToString() });
}

public sealed class SetCustomerStatusValidator : AbstractValidator<SetCustomerStatusCommand>
{
    public SetCustomerStatusValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Status).IsInEnum();
    }
}

public class SetCustomerStatusHandler : IRequestHandler<SetCustomerStatusCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SetCustomerStatusHandler(ICustomerRepository customers, IUnitOfWork uow, TimeProvider clock)
    {
        _customers = customers; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(SetCustomerStatusCommand cmd, CancellationToken ct)
    {
        var customer = await _customers.GetByIdAsync(cmd.Id, ct);
        if (customer is null) return Result.Failure(Error.NotFound("العميل غير موجود"));

        if (cmd.Status == CustomerStatus.Blocked) customer.Block(_clock.GetUtcNow().UtcDateTime);
        else customer.Unblock();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// تصدير بيانات عميل بطلبه (قابلية نقل البيانات) — مُدقَّق لأنه يكشف بيانات شخصية كاملة.
public record ExportCustomerDataQuery(int Id) : IRequest<Result<CustomerExportDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("customer.data-exported", "Customer", Id.ToString());
}

public class ExportCustomerDataHandler : IRequestHandler<ExportCustomerDataQuery, Result<CustomerExportDto>>
{
    private readonly ICustomerQueries _queries;
    private readonly TimeProvider _clock;

    public ExportCustomerDataHandler(ICustomerQueries queries, TimeProvider clock)
    {
        _queries = queries; _clock = clock;
    }

    public async Task<Result<CustomerExportDto>> Handle(ExportCustomerDataQuery q, CancellationToken ct) =>
        await _queries.ExportAsync(q.Id, _clock.GetUtcNow().UtcDateTime, ct) is { } export
            ? Result<CustomerExportDto>.Success(export)
            : Result<CustomerExportDto>.Failure(Error.NotFound("العميل غير موجود"));
}

// محو عميل بطلبه (حقّ الحذف) — لا رجعة فيه، مُدقَّق.
public record EraseCustomerCommand(int Id) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("customer.erased", "Customer", Id.ToString());
}

public class EraseCustomerHandler : IRequestHandler<EraseCustomerCommand, Result>
{
    private readonly ICustomerRepository _customers;
    private readonly CustomerErasure _erasure;

    public EraseCustomerHandler(ICustomerRepository customers, CustomerErasure erasure)
    {
        _customers = customers; _erasure = erasure;
    }

    public async Task<Result> Handle(EraseCustomerCommand cmd, CancellationToken ct)
    {
        var customer = await _customers.GetByIdAsync(cmd.Id, ct);
        if (customer is null) return Result.Failure(Error.NotFound("العميل غير موجود"));

        await _erasure.EraseAsync(customer, ct);
        return Result.Success();
    }
}
