using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Persistence;

namespace Souq.IntegrationTests;

// ============================================================================
// "لا بيانات بطاقة تُخزَّن أبداً" (معيار خروج المرحلة 11): نموذج EF كاملاً بلا عمود يشبه بيانات بطاقة، وجداول الدفع بأعمدتها
// المراجَعة وحدها — عمود جديد فيها قرار صريح يمرّ بهذا الاختبار. لا قاعدة ولا شبكة: النموذج يُبنى من الإعدادات وحدها.
// ============================================================================
public class PaymentDataRulesTests
{
    private static readonly Regex CardLike =
        new(@"card|^pan$|cvc|cvv|last4|exp(iry)?(month|year)|iban|track[12]", RegexOptions.IgnoreCase);

    private static AppDbContext ModelOnly() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=model-only;Database=none").Options, new TenantContext());

    // الأعمدة كما تُخزَّن في الجدول نفسه: مفتاح النوع المملوك (Money) يُطابَق على عمود Id مالكه، لا باسم خاصيّته.
    private static string[] ColumnsOf(AppDbContext db, string table)
    {
        var store = StoreObjectIdentifier.Table(table, null);
        return db.Model.GetEntityTypes()
            .Where(t => t.GetTableName() == table)
            .SelectMany(t => t.GetProperties().Select(p => p.GetColumnName(store)))
            .OfType<string>().Distinct().Order().ToArray();
    }

    [Fact]
    public void لا_عمود_يشبه_بيانات_بطاقة_في_أي_جدول()
    {
        using var db = ModelOnly();
        var columns = db.Model.GetEntityTypes().SelectMany(t => t.GetProperties().Select(p => p.GetColumnName())).ToList();

        columns.Should().HaveCountGreaterThan(50);
        columns.Where(c => CardLike.IsMatch(c)).Should().BeEmpty();
    }

    [Fact]
    public void جداول_الدفع_بأعمدتها_المراجَعة_وحدها()
    {
        using var db = ModelOnly();

        ColumnsOf(db, "Payments").Should().Equal(new[]
        {
            // "GatewayAccount" أُضيف بقرارٍ مُراجَع (TD-50، ADR-0061): المفتاحُ العلني للحساب الذي
            // قبض — علنيٌّ بحكم تعريفه، يُرسله المزوّد إلى كلّ متصفّح. **ليس سرّاً ولا بيانةَ بطاقة**،
            // وهو ما يجعل إضافته هنا مقبولة أصلاً. وهذا السطرُ هو الحارس: عمودٌ يدخل هذه الجداول
            // بلا مراجعةٍ يُسقط البناء.
            "Amount", "CreatedAt", "Currency", "Gateway", "GatewayAccount", "Id", "OrderId", "PendingRefundAmount", "ProviderPaymentId",
            "RefundedAmount", "RowVersion", "Status", "TenantId", "UpdatedAt",
        }.Order());
        ColumnsOf(db, "Refunds").Should().Equal(new[]
        {
            "Amount", "CompletedAt", "CreatedAt", "Currency", "FailureReason", "Id", "PaymentId", "ProviderRefundId", "Reason",
            "RequestedByUserId", "Status", "TenantId", "UpdatedAt",
        }.Order());
        // مفاتيح المتجر: العلني كما هو، والسرّان مشفَّران فقط — لا عمود لنصّهما.
        ColumnsOf(db, "StorePaymentAccounts").Should().Equal(new[]
        {
            "CreatedAt", "Id", "LiveMode", "Provider", "PublishableKey", "SecretKeyCipher", "SecretKeyHint", "TenantId",
            "UpdatedAt", "UpdatedByUserId", "WebhookSecretCipher",
        }.Order());
    }
}
