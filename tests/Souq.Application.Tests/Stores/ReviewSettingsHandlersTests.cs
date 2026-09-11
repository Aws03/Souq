using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Stores;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.Stores;

// سياسة نشر التقييمات (المرحلة 13): تُقرأ وتُعدَّل على متجر السياق وحده — لا معرّف متجر في الطلب.
public class ReviewSettingsHandlersTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Tenant _store = new("متجر اختبار", "test-store", "JOD", "ar", "Asia/Amman");

    public ReviewSettingsHandlersTests() => _tenants.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_store);

    [Fact]
    public async Task السياسة_تُقرأ_وتُعدَّل_على_متجر_السياق_وحده()
    {
        var context = TestTenant.Context(id: 1);
        (await new GetReviewSettingsHandler(_tenants, context).Handle(new GetReviewSettingsQuery(), CancellationToken.None))
            .AutoApprove.Should().BeFalse();

        var result = await new UpdateReviewSettingsHandler(_tenants, context, _uow)
            .Handle(new UpdateReviewSettingsCommand(AutoApprove: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _store.ReviewsAutoApprove.Should().BeTrue();
        await _tenants.DidNotReceive().GetByIdAsync(Arg.Is<int>(id => id != 1), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void التعديل_يُدقَّق_بالقيمة_الجديدة()
    {
        var record = new UpdateReviewSettingsCommand(AutoApprove: true).ToAuditRecord();

        record.Action.Should().Be("store.reviews.updated");
        record.Metadata!["autoApprove"].Should().Be(true);
    }
}
