using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Reviews.Moderation;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Reviews;

// الإشراف (المرحلة 13): القرار على تقييم من متجر السياق وحده (غيره ⇒ 404 بلا حفظ — مرشّح المستأجر يُثبَت في التكامل)،
// ويُسجَّل باسم المشرف الحالي ووقت الساعة. الملاحظة لا تدخل سجلّ التدقيق.
public class ReviewModerationHandlersTests
{
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = new();
    private readonly Review _review = TestCatalog.WithId(new Review(productId: 5, customerId: 1, orderId: 9, rating: 4, "جيد"), 3);

    public ReviewModerationHandlersTests() => _reviews.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(_review);

    [Fact]
    public async Task الاعتماد_ينشر_التقييم_باسم_المشرف_الحالي()
    {
        var result = await new ApproveReviewHandler(_reviews, TestCurrentUser.Staff(901), _clock, _uow)
            .Handle(new ApproveReviewCommand(3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _review.Status.Should().Be(ReviewStatus.Approved);
        _review.ModeratedByUserId.Should().Be(901);
        _review.ModeratedAt.Should().Be(_clock.UtcNow);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الرفض_يخفي_التقييم_ويحفظ_ملاحظة_المشرف()
    {
        var result = await new RejectReviewHandler(_reviews, TestCurrentUser.Admin(900), _clock, _uow)
            .Handle(new RejectReviewCommand(3, "خارج الموضوع"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _review.Status.Should().Be(ReviewStatus.Rejected);
        _review.ModerationNote.Should().Be("خارج الموضوع");
        _review.ModeratedByUserId.Should().Be(900);
    }

    [Fact]
    public async Task تقييم_ليس_في_المتجر_404_بلا_حفظ()
    {
        var result = await new ApproveReviewHandler(_reviews, TestCurrentUser.Staff(), _clock, _uow)
            .Handle(new ApproveReviewCommand(77), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بلا_مستخدم_مُصادَق_لا_قرار()
    {
        var act = () => new RejectReviewHandler(_reviews, TestCurrentUser.Anonymous(), _clock, _uow)
            .Handle(new RejectReviewCommand(3, null), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationRequiredException>();
        _review.Status.Should().Be(ReviewStatus.Pending);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ملاحظة_الرفض_اختيارية_وحتى_500_حرف()
    {
        var validator = new RejectReviewValidator();

        validator.Validate(new RejectReviewCommand(3, null)).IsValid.Should().BeTrue();
        validator.Validate(new RejectReviewCommand(3, new string('x', Review.ModerationNoteMaxLength + 1))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void القرار_يُدقَّق_بمعرّف_التقييم_بلا_نصّ_الملاحظة()
    {
        var record = new RejectReviewCommand(3, "سبب خاص").ToAuditRecord();

        record.Action.Should().Be("review.rejected");
        record.TargetType.Should().Be("Review");
        record.TargetId.Should().Be("3");
        record.Metadata.Should().BeNull();
        new ApproveReviewCommand(3).ToAuditRecord().Action.Should().Be("review.approved");
    }
}
