using AwesomeAssertions;
using MediatR;
using NSubstitute;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Behaviors;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Auditing;

namespace Souq.Application.Tests.Common;

// سلوك التدقيق (D-17): السطر يُدرج قبل المعالج بالفاعل والمنطقة والمتجر والعنوان، ويُحفظ بعد النجاح ويُسقط بعد
// الفشل أو الاستثناء؛ الطلب غير المُدقَّق يمرّ بلا أثر؛ والبيانات الوصفية ما اختاره الطلب فقط.
public class AuditBehaviorTests
{
    private sealed record AuditedCommand(string Password, bool Succeed = true) : IRequest<Result>, IAuditable
    {
        public AuditRecord ToAuditRecord() => new("store.settings.updated", "Tenant", "7",
            Metadata: new Dictionary<string, object?> { ["field"] = "colors" });
    }

    private sealed record PlatformCommand : IRequest<Result>, IAuditable
    {
        public AuditRecord ToAuditRecord() => new("tenant.suspended", "Tenant", "9", TenantId: 9);
    }

    private sealed record PlainQuery : IRequest<string>;

    private readonly IAuditTrail _trail = Substitute.For<IAuditTrail>();
    private readonly IClientInfo _client = Substitute.For<IClientInfo>();
    private AuditEntry? _staged;

    public AuditBehaviorTests()
    {
        _client.IpAddress.Returns("203.0.113.9");
        _trail.When(t => t.Stage(Arg.Any<AuditEntry>())).Do(call => _staged = call.Arg<AuditEntry>());
    }

    private AuditBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>(ITenantContext tenancy) where TRequest : notnull =>
        new(_trail, TestCurrentUser.Admin(42), tenancy, _client, TimeProvider.System);

    [Fact]
    public async Task أمر_ناجح_يُدرج_سطره_بالفاعل_والمتجر_ثم_يُحفظ()
    {
        var behavior = Behavior<AuditedCommand, Result>(TestTenant.Context(id: 5));

        var result = await behavior.Handle(new AuditedCommand("p@ss-SECRET"), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _staged.Should().NotBeNull();
        _staged!.Action.Should().Be("store.settings.updated");
        _staged.Area.Should().Be(AuditAreas.Store);
        _staged.TenantId.Should().Be(5);
        _staged.ActorUserId.Should().Be(42);
        _staged.ActorRole.Should().Be("TenantAdmin");
        _staged.TargetId.Should().Be("7");
        _staged.IpAddress.Should().Be("203.0.113.9");
        _staged.Metadata.Should().Contain("colors").And.NotContain("p@ss-SECRET");
        await _trail.Received(1).FlushAsync(Arg.Any<CancellationToken>());
        _trail.DidNotReceive().Discard();
    }

    [Fact]
    public async Task نتيجة_فاشلة_تُسقط_السطر()
    {
        var behavior = Behavior<AuditedCommand, Result>(TestTenant.Context());

        await behavior.Handle(new AuditedCommand("x"),
            () => Task.FromResult(Result.Failure(Error.NotFound("غير موجود"))), CancellationToken.None);

        _trail.Received(1).Discard();
        await _trail.DidNotReceive().FlushAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task استثناء_يُسقط_السطر_ويرتفع_كما_هو()
    {
        var behavior = Behavior<AuditedCommand, Result>(TestTenant.Context());

        var act = () => behavior.Handle(new AuditedCommand("x"),
            () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _trail.Received(1).Discard();
    }

    [Fact]
    public async Task طلب_المنصّة_يحمل_المتجر_المستهدف_ومنطقة_المنصّة()
    {
        var platform = new TenantContext();
        platform.UsePlatform();
        var behavior = Behavior<PlatformCommand, Result>(platform);

        await behavior.Handle(new PlatformCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        _staged!.Area.Should().Be(AuditAreas.Platform);
        _staged.TenantId.Should().Be(9);
    }

    [Fact]
    public async Task طلب_غير_مُدقَّق_لا_يترك_أثراً()
    {
        var behavior = Behavior<PlainQuery, string>(TestTenant.Context());

        (await behavior.Handle(new PlainQuery(), () => Task.FromResult("ok"), CancellationToken.None)).Should().Be("ok");

        _trail.DidNotReceive().Stage(Arg.Any<AuditEntry>());
        await _trail.DidNotReceive().FlushAsync(Arg.Any<CancellationToken>());
    }
}
