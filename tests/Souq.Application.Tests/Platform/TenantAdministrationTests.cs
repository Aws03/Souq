using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing.Contracts;
using Souq.Application.Features.Platform;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.Platform;

// تنسيق إدارة المتاجر من المنصّة: المعرّف فريد على المنصّة، كل تغيير يُبطل دليل المتاجر، العملة تُقفل بعد النشاط
// التجاري، ودعوة المدير تحتاج نطاقاً وتعمل داخل نطاق خدمات المتجر المستهدف.
public class TenantAdministrationTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantDirectory _directory = Substitute.For<ITenantDirectory>();
    private readonly IPlatformQueries _queries = Substitute.For<IPlatformQueries>();
    private readonly ITenantScopeRunner _scopes = Substitute.For<ITenantScopeRunner>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IStoreEntitlements _entitlements = Substitute.For<IStoreEntitlements>();

    private static Tenant Acme() => new("Acme", "acme", "JOD", "ar", "Asia/Amman");

    private CreateTenantHandler Creating() => new(_tenants, _entitlements, _directory, _uow);

    [Fact]
    public async Task معرّف_مستخدم_لمتجر_آخر_يُرفض_بلا_حفظ()
    {
        _tenants.SlugExistsAsync("acme", Arg.Any<CancellationToken>()).Returns(true);

        var result = await Creating().Handle(
            new CreateTenantCommand("Acme", "ACME", "JOD", "ar", "Asia/Amman"), CancellationToken.None);

        result.ErrorCode.Should().Be("TenantSlugTaken");
        await _tenants.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task متجر_جديد_يُحفظ_ويُبطل_الدليل()
    {
        var result = await Creating().Handle(
            new CreateTenantCommand("Acme", "acme", "JOD", "ar", "Asia/Amman"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _tenants.Received(1).AddAsync(Arg.Is<Tenant>(t => t.Status == TenantStatus.Provisioning), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _directory.Received(1).Invalidate();
    }

    [Fact]
    public async Task تغيير_العملة_بعد_نشاط_تجاري_يرفضه_التجمّع()
    {
        _tenants.GetByIdAsync(0, Arg.Any<CancellationToken>()).Returns(Acme());
        _queries.HasCommercialActivityAsync(0, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => new UpdateTenantHandler(_tenants, _queries, _directory, _uow)
            .Handle(new UpdateTenantCommand(0, "Acme", "USD"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTenantOperationException>();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task دعوة_مدير_لمتجر_بلا_نطاق_تُرفض()
    {
        _tenants.GetByIdAsync(0, Arg.Any<CancellationToken>()).Returns(Acme());
        _directory.FindByIdAsync(0, Arg.Any<CancellationToken>()).Returns(Info());

        var result = await new InviteTenantAdminHandler(_tenants, _directory, _scopes).Handle(
            new InviteTenantAdminCommand(0, "المدير", "boss@acme.test"), CancellationToken.None);

        result.ErrorCode.Should().Be("TenantHasNoDomain");
        await _scopes.DidNotReceiveWithAnyArgs()
            .RunAsync<AccountInvitations, Result<InvitationResult>>(default!, default!);
    }

    [Fact]
    public async Task دعوة_المدير_تعمل_داخل_نطاق_المتجر_المستهدف()
    {
        var acme = Acme();
        acme.AddDomain("shop.acme.test");
        _tenants.GetByIdAsync(0, Arg.Any<CancellationToken>()).Returns(acme);
        _directory.FindByIdAsync(0, Arg.Any<CancellationToken>()).Returns(Info());
        _scopes.RunAsync(Arg.Any<TenantInfo>(), Arg.Any<Func<AccountInvitations, Task<Result<InvitationResult>>>>())
            .Returns(Result<InvitationResult>.Success(new InvitationResult(7, false)));

        var result = await new InviteTenantAdminHandler(_tenants, _directory, _scopes).Handle(
            new InviteTenantAdminCommand(0, "المدير", "boss@acme.test"), CancellationToken.None);

        result.Value!.UserId.Should().Be(7);
        await _scopes.Received(1).RunAsync(
            Arg.Is<TenantInfo>(t => t.Slug == "acme"), Arg.Any<Func<AccountInvitations, Task<Result<InvitationResult>>>>());
    }

    private static TenantInfo Info() => new(0, "acme", "Acme", TenantStatus.Provisioning, "JOD", "ar", "Asia/Amman", new HashSet<string>(StringComparer.Ordinal));
}
