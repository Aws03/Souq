using AwesomeAssertions;
using Souq.Application.Common.Notifications;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.Notifications;

// ============================================================================
// هل تُسلَّم رسالة الصادر ومتجرُها مغلق؟ (TD-67 — وقد كان الصندوق لا يسأل أصلاً.)
//
// القاعدة كلّها في سطرين، فما يستحقّ اختباراً هو **الحدود** لا الشكل:
//   • الموقوف لا يمنع شيئاً، وهذا نتيجةُ قرار المالك C-17 = B لا تهاوناً — للمتجر الموقوف تاجرٌ
//     يعمل ومشترٍ يتتبّع طلباً دفع ثمنه، فحجبُ بريد الشحن عنه مع إتاحة التتبّع له تناقض.
//   • المؤرشف يمنع كل شيء **إلا رسالتَي أمن الحساب** — وهذا هو الحدّ الذي يسهل أن يُكسَر لاحقاً
//     بقاعدةٍ أبسط ("المغلق لا يُرسل")، فيُحبَس صاحب حسابٍ خارجه بلا طريق عودة.
// ============================================================================
public class OutboxStorePolicyTests
{
    private static readonly string[] AccountSecurity =
        [nameof(PasswordResetRequested), nameof(PasswordChanged)];

    private static readonly string[] StoreBusiness =
        [nameof(OrderEmailRequested), nameof(EmailVerificationRequested), nameof(AccountInvited),
         "OrderStatusChanged", "StockBecameLow"];

    [Theory]
    [InlineData(TenantStatus.Active)]
    [InlineData(TenantStatus.Provisioning)]
    [InlineData(TenantStatus.Suspended)]
    public void المتجر_غير_المؤرشف_يُسلّم_كل_أنواع_رسائله(TenantStatus status)
    {
        foreach (var type in AccountSecurity.Concat(StoreBusiness))
            OutboxStorePolicy.MayDeliver(type, status).Should().BeTrue($"{type} تُسلَّم والمتجر {status}");
    }

    [Fact]
    public void المؤرشف_يمنع_رسائل_المتجر_ويُسلّم_أمن_الحساب()
    {
        foreach (var type in StoreBusiness)
            OutboxStorePolicy.MayDeliver(type, TenantStatus.Archived).Should()
                .BeFalse($"{type} رسالةٌ من متجر لن يعود");

        foreach (var type in AccountSecurity)
            OutboxStorePolicy.MayDeliver(type, TenantStatus.Archived).Should()
                .BeTrue($"{type} عن الشخص لا عن المتجر — من حُبس خارج حسابه يحقّ له استعادته");
    }

    // كل نوعٍ مسجَّل في الصندوق مصنَّفٌ هنا عمداً: نوعٌ جديد يُضاف ولا يُذكر في أيّ من القائمتين
    // يأخذ سلوك المؤرشف بالصمت — وهو قرارٌ يجب أن يُتَّخذ، لا أن يُورَث من ترتيب `switch`.
    [Fact]
    public void كل_نوع_رسالة_مصنَّف_في_هذا_الاختبار()
    {
        var classified = AccountSecurity.Concat(StoreBusiness).ToHashSet(StringComparer.Ordinal);
        var registered = new[]
        {
            typeof(PasswordResetRequested), typeof(PasswordChanged), typeof(EmailVerificationRequested),
            typeof(AccountInvited), typeof(OrderEmailRequested),
            typeof(Souq.Domain.Events.OrderStatusChanged), typeof(Souq.Domain.Events.StockBecameLow),
        }.Select(t => t.Name);

        registered.Where(name => !classified.Contains(name)).Should()
            .BeEmpty("نوع رسالة جديد يلزمه قرارٌ صريح: هل يصل من متجر مؤرشف؟");
    }
}
