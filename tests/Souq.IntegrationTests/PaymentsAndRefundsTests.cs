using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Infrastructure.Payments;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// المدفوعات والاسترداد (المرحلة 11) عبر HTTP وSQL Server الحقيقيين: الدفعة تُسجَّل مع الطلب وتُحسم بالدفع، الاستردادات
// الجزئية والكاملة لا تتجاوز المدفوع ولو تزامنت، إلغاء طلب مدفوع يردّ ماله، إشعار حساب النشر يصل متجره ولو جاء على مضيف
// آخر، وحساب المتجر يُحفظ مشفّراً ولا يعود سرّه ويُدقَّق. العزل بين المتاجر في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class PaymentsAndRefundsTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public PaymentsAndRefundsTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الدفع_يسجّل_دفعته_والاستردادات_لا_تتجاوز_المدفوع_وكلٌّ_يرى_ما_يخصّه()
    {
        var admin = await _api.AdminAsync();
        var (customer, _) = await _api.NewCustomerAsync();
        var orderId = await PaidOrderAsync(_api, admin, customer, price: 20m);

        var payment = await PaymentAsync(_api, orderId);
        (payment.Status, payment.Gateway, payment.Amount.Amount).Should().Be((PaymentStatus.Succeeded, FakeGateway.GatewayName, 20m));

        (await RefundAsync(admin, orderId, new { amount = 5m, reason = "منتج تالف" })).Should().Be(("Succeeded", 5m));
        (await ProblemAsync(await admin.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { amount = 20m })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "RefundExceedsPayment"));
        (await RefundAsync(admin, orderId, new { })).Should().Be(("Succeeded", 15m));
        (await ProblemAsync(await admin.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "NothingToRefund"));

        var asAdmin = (await admin.GetFromJsonAsync<OrderBody>($"/api/orders/{orderId}", TestApi.Json))!;
        (asAdmin.Payment!.RefundedAmount, asAdmin.Payment.Refundable, asAdmin.CanRefund).Should().Be((20m, 0m, false));
        asAdmin.Payment.Refunds.Select(r => (r.Amount, r.Status, r.Reason)).Should()
            .Equal((5m, "Succeeded", "منتج تالف"), (15m, "Succeeded", (string?)null));

        var asOwner = (await customer.GetFromJsonAsync<OrderBody>($"/api/orders/{orderId}", TestApi.Json))!;
        (asOwner.Payment!.Status, asOwner.Payment.RefundedAmount, asOwner.Payment.Refunds.Count, asOwner.CanRefund)
            .Should().Be(("Succeeded", 20m, 0, false));
        (await customer.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task استردادات_متزامنة_لا_تتجاوز_المدفوع()
    {
        var admin = await _api.AdminAsync();
        var (customer, _) = await _api.NewCustomerAsync();
        var orderId = await PaidOrderAsync(_api, admin, customer, price: 20m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => admin.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { amount = 10m })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(2);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
            (await ProblemAsync(rejected)).Should().Be((HttpStatusCode.UnprocessableEntity, "RefundExceedsPayment"));
        var payment = await PaymentAsync(_api, orderId);
        (payment.RefundedAmount, payment.PendingRefundAmount, payment.Refunds.Count(r => r.Status == RefundStatus.Succeeded))
            .Should().Be((20m, 0m, 2));
    }

    [Fact]
    public async Task إلغاء_الإدارة_لطلب_مدفوع_يردّ_ماله_وغير_المدفوع_تُلغى_دفعته()
    {
        var admin = await _api.AdminAsync();
        var (customer, _) = await _api.NewCustomerAsync();
        var paidId = await PaidOrderAsync(_api, admin, customer, price: 30m);
        var pendingId = await PlacedOrderAsync(_api, customer, await _api.CreateProductAsync(admin, price: 7m, stock: 3));

        (await admin.PutAsJsonAsync($"/api/orders/{paidId}/status", new { action = "Cancel", note = "نفد من المورّد" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/api/orders/{pendingId}/status", new { action = "Cancel" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var paid = await PaymentAsync(_api, paidId);
        var refund = paid.Refunds.Should().ContainSingle().Subject;
        (paid.Status, paid.RefundedAmount, refund.Status, refund.Reason).Should()
            .Be((PaymentStatus.Succeeded, 30m, RefundStatus.Succeeded, "نفد من المورّد"));
        refund.RequestedByUserId.Should().NotBeNull("الموظّف الذي ألغى يُسجَّل على الاسترداد");

        var pending = await PaymentAsync(_api, pendingId);
        (pending.Status, pending.Refunds.Count).Should().Be((PaymentStatus.Cancelled, 0));
    }

    [Fact]
    public async Task إشعار_حساب_النشر_يُطبَّق_في_متجر_الطلب_ولو_وصل_على_مضيف_آخر_والمزوَّر_يُرفض()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var storeAdmin = await storeApi.AdminAsync();
        var (storeCustomer, _) = await storeApi.NewCustomerAsync();
        var orderId = await PlacedOrderAsync(storeApi, storeCustomer, await storeApi.CreateProductAsync(storeAdmin, price: 12m, stock: 3));
        var intentId = await storeApi.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.PaymentIntentId!).SingleAsync());
        var payload = JsonSerializer.Serialize(new
        {
            type = "payment_intent.succeeded", paymentIntentId = intentId, orderReference = orderId.ToString(), tenantId = store.Tenant.Id,
        });

        var forged = await PostWebhookAsync(_api.Anonymous(), payload, FakeGateway.Sign(payload, "not-the-secret"));
        (await ProblemAsync(forged)).Should().Be((HttpStatusCode.BadRequest, "InvalidSignature"));
        (await OrderStatusAsync(storeApi, orderId)).Should().Be(OrderStatus.Pending);

        // على مضيف المتجر الافتراضي (رابط حساب النشر الواحد)، والطلب في المتجر الآخر.
        (await PostWebhookAsync(_api.Anonymous(), payload, FakeGateway.Sign(payload, SouqApiFactory.FakeWebhookSecret)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await OrderStatusAsync(storeApi, orderId)).Should().Be(OrderStatus.Paid);
        (await PaymentAsync(storeApi, orderId)).Status.Should().Be(PaymentStatus.Succeeded);

        // تكرار الإشعار (على مضيف المتجر نفسه هذه المرة) بلا أثر ثانٍ.
        (await PostWebhookAsync(storeApi.Anonymous(), payload, FakeGateway.Sign(payload, SouqApiFactory.FakeWebhookSecret)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await storeApi.WithDbAsync(db => db.OrderStatusHistories.CountAsync(h => EF.Property<int>(h, "OrderId") == orderId
            && h.Status == OrderStatus.Paid))).Should().Be(1);
    }

    [Fact]
    public async Task حساب_المتجر_يُحفظ_مشفّراً_ولا_يعود_سرّه_ويُدقَّق_والمنصّة_تضبطه_داخل_نطاقه()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var storeAdmin = await storeApi.AdminAsync();
        const string secret = "sk_test_51IntegrationSecretXyz9";

        (await storeAdmin.PutAsJsonAsync("/api/admin/store/payments",
                new { publishableKey = "pk_test_51IntegrationPublic", secretKey = secret, webhookSecret = "whsec_integration_hook" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = await storeApi.WithDbAsync(db => db.StorePaymentAccounts.AsNoTracking().SingleAsync());
        row.SecretKeyCipher.Should().StartWith($"v1.{SouqApiFactory.SecretsKeyId}.").And.NotContain(secret);
        row.WebhookSecretCipher.Should().NotContain("whsec_integration_hook");

        var raw = await storeAdmin.GetStringAsync("/api/admin/store/payments");
        raw.Should().NotContain(secret).And.NotContain("whsec_integration_hook").And.NotContain(row.SecretKeyCipher);
        var view = JsonSerializer.Deserialize<AccountBody>(raw, TestApi.Json)!;
        (view.UsesStoreAccount, view.SecretKeyHint, view.HasWebhookSecret, view.LiveMode).Should().Be((true, "…Xyz9", true, (bool?)false));

        // Stripe.js يُهيَّأ بمفتاح حساب المتجر؛ المتجر الافتراضي باقٍ على حساب النشر (التجريبي هنا: بلا مفتاح).
        (await storeApi.Anonymous().GetFromJsonAsync<ConfigBody>("/api/payments/config", TestApi.Json))!
            .PublishableKey.Should().Be("pk_test_51IntegrationPublic");
        (await _api.Anonymous().GetFromJsonAsync<ConfigBody>("/api/payments/config", TestApi.Json))!.PublishableKey.Should().BeEmpty();

        var audit = await storeApi.WithDbAsync(db => db.AuditEntries
            .Where(a => a.Action == "store.payments.updated" && a.TenantId == store.Tenant.Id).Select(a => a.Metadata).SingleAsync());
        audit.Should().Contain("secretKeyChanged").And.NotContain("sk_test").And.NotContain("whsec_");

        // المنصّة: تعديل المفتاح العلني داخل نطاق المتجر — والسرّ المحفوظ باقٍ.
        var platform = await _api.PlatformOwnerAsync();
        (await platform.PutAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/payments",
            new { publishableKey = "pk_test_51PlatformPublic" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await storeApi.WithDbAsync(db => db.StorePaymentAccounts.AsNoTracking().SingleAsync());
        (updated.PublishableKey, updated.SecretKeyCipher).Should().Be(("pk_test_51PlatformPublic", row.SecretKeyCipher));
        (await platform.GetFromJsonAsync<AccountBody>($"/api/platform/tenants/{store.Tenant.Id}/payments", TestApi.Json))!
            .SecretKeyHint.Should().Be("…Xyz9");

        // مفتاح بصيغة خاطئة يُرفض ولا يغيّر شيئاً؛ فكّ الربط يعيد حساب النشر.
        (await ProblemAsync(await storeAdmin.PutAsJsonAsync("/api/admin/store/payments", new { publishableKey = "pk_live_51Mixed", secretKey = secret })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidPaymentKeys"));
        (await storeAdmin.DeleteAsync("/api/admin/store/payments")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await storeApi.WithDbAsync(db => db.StorePaymentAccounts.CountAsync())).Should().Be(0);
    }

    private static async Task<int> PaidOrderAsync(TestApi api, HttpClient admin, HttpClient customer, decimal price)
    {
        var orderId = await PlacedOrderAsync(api, customer, await api.CreateProductAsync(admin, price: price, stock: 5));
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        return orderId;
    }

    private static async Task<int> PlacedOrderAsync(TestApi api, HttpClient customer, int productId)
    {
        var response = await api.PlaceOrderAsync(customer, productId, 1);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
    }

    private static async Task<(string Status, decimal Amount)> RefundAsync(HttpClient admin, int orderId, object body)
    {
        var response = await admin.PostAsJsonAsync($"/api/orders/{orderId}/refunds", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var outcome = (await response.Content.ReadFromJsonAsync<OutcomeBody>(TestApi.Json))!;
        return (outcome.Status, outcome.Amount);
    }

    private static Task<Payment> PaymentAsync(TestApi api, int orderId) =>
        api.WithDbAsync(db => db.Payments.AsNoTracking().Include(p => p.Refunds).SingleAsync(p => p.OrderId == orderId));

    private static Task<OrderStatus> OrderStatusAsync(TestApi api, int orderId) =>
        api.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync());

    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string payload, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", signature);
        return client.SendAsync(request);
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record OrderBody(int Id, string Status, PaymentBody? Payment, bool CanRefund);
    private sealed record PaymentBody(
        string Status, decimal Amount, decimal RefundedAmount, decimal Refundable, string Currency, List<RefundBody> Refunds);
    private sealed record RefundBody(int Id, decimal Amount, string Status, string? Reason, string? FailureReason);
    private sealed record OutcomeBody(int RefundId, string Status, decimal Amount, string Currency, string? FailureReason);
    private sealed record AccountBody(
        bool UsesStoreAccount, string? PublishableKey, bool? LiveMode, string? SecretKeyHint, bool HasWebhookSecret, bool CanStoreSecrets);
    private sealed record ConfigBody(string PublishableKey);
}
