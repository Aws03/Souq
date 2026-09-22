using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Exceptions;
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
        (payment.Status, payment.Gateway, payment.Amount.Amount).Should().Be((PaymentStatus.Succeeded, DemoPaymentGateway.GatewayName, 20m));

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

        var forged = await PostWebhookAsync(_api.Anonymous(), payload, DemoPaymentGateway.Sign(payload, "not-the-secret"));
        (await ProblemAsync(forged)).Should().Be((HttpStatusCode.BadRequest, "InvalidSignature"));
        (await OrderStatusAsync(storeApi, orderId)).Should().Be(OrderStatus.Pending);

        // على مضيف المتجر الافتراضي (رابط حساب النشر الواحد)، والطلب في المتجر الآخر.
        (await PostWebhookAsync(_api.Anonymous(), payload, DemoPaymentGateway.Sign(payload, SouqApiFactory.DemoWebhookSecret)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await OrderStatusAsync(storeApi, orderId)).Should().Be(OrderStatus.Paid);
        (await PaymentAsync(storeApi, orderId)).Status.Should().Be(PaymentStatus.Succeeded);

        // تكرار الإشعار (على مضيف المتجر نفسه هذه المرة) بلا أثر ثانٍ.
        (await PostWebhookAsync(storeApi.Anonymous(), payload, DemoPaymentGateway.Sign(payload, SouqApiFactory.DemoWebhookSecret)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await storeApi.WithDbAsync(db => db.OrderStatusHistories.CountAsync(h => EF.Property<int>(h, "OrderId") == orderId
            && h.Status == OrderStatus.Paid))).Should().Be(1);
    }

    // ============================================================================
    // R-02: نجاح متأخّر على طلب أُلغي. قبل الإصلاح كان التأكيد يعود بنجاح صامت لأن الطلب ليس Pending، فتبقى الدفعة
    // غير ناجحة ويرفض كل مسار استرداد ردّ المال — يُستردّ من لوحة Stripe يدوياً وحدها. الآن: الحقيقة تُسجَّل على
    // الدفعة فتصير قابلة للاسترداد من شاشة الطلب، والطلب يبقى ملغى.
    // ============================================================================
    [Fact]
    public async Task نجاح_نيّة_بعد_إلغاء_الطلب_يُسجَّل_على_الدفعة_فيُستردّ_ولا_يبقى_المال_خارج_النظام()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var storeAdmin = await storeApi.AdminAsync();
        var (customer, _) = await storeApi.NewCustomerAsync();
        var orderId = await PlacedOrderAsync(storeApi, customer, await storeApi.CreateProductAsync(storeAdmin, price: 25m, stock: 3));
        var intentId = await storeApi.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.PaymentIntentId!).SingleAsync());

        (await customer.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "غيّرت رأيي" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PaymentAsync(storeApi, orderId)).Status.Should().Be(PaymentStatus.Cancelled);

        // البوّابة تقول بعدها إن النيّة نجحت (بطاقة أُعيدت المحاولة عليها قبل أن يصل الإلغاء).
        var payload = JsonSerializer.Serialize(new
        {
            type = "payment_intent.succeeded", paymentIntentId = intentId, orderReference = orderId.ToString(), tenantId = store.Tenant.Id,
        });
        (await PostWebhookAsync(storeApi.Anonymous(), payload, DemoPaymentGateway.Sign(payload, SouqApiFactory.DemoWebhookSecret)))
            .StatusCode.Should().Be(HttpStatusCode.OK, "الإشعار يُقرّ به كي لا تعيد البوّابة إرساله إلى الأبد");

        (await OrderStatusAsync(storeApi, orderId)).Should().Be(OrderStatus.Cancelled, "الطلب لا يعود للحياة");
        (await PaymentAsync(storeApi, orderId)).Status.Should().Be(PaymentStatus.Succeeded, "المال قُبض فعلاً — تُسجَّل الحقيقة");

        // وهذا هو المكسب: المال صار قابلاً للاسترداد من مسار الاسترداد العادي بصلاحيته.
        (await RefundAsync(storeAdmin, orderId, new { })).Should().Be(("Succeeded", 25m));
        var settled = await PaymentAsync(storeApi, orderId);
        (settled.RefundedAmount, settled.Refundable.Amount).Should().Be((25m, 0m));

        // تكرار الإشعار لا يسجّل قبضاً ثانياً ولا يغيّر شيئاً.
        (await PostWebhookAsync(storeApi.Anonymous(), payload, DemoPaymentGateway.Sign(payload, SouqApiFactory.DemoWebhookSecret)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PaymentAsync(storeApi, orderId)).Refunds.Should().ContainSingle();
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

    // ============================================================================
    // TD-62 (P0) — سرّ متجر لا يُفكّ **يُسقط الدفع بصوت**، ولا يرجع صامتاً إلى حساب النشر.
    //
    // هذا هو أخطر ما يمكن أن يخطئ فيه هذا الملف كلّه، وهو بالضبط ما وُجدت ADR-0031 لمنعه: لو
    // تسرّب المسار من الاستثناء إلى `?? _deployment.Gateway` لصار مال بطاقات **متجرٍ** يُقبض في
    // حساب **النشر** — أموال شخص في حساب شخص آخر، بلا خطأ ولا سجلّ ولا اختبار أحمر. المسار كُتب
    // دفاعياً ولم يُمسّ قطّ: `PaymentGatewayUnavailableException` لم تكن لها إشارة واحدة في أي اختبار.
    //
    // ولا شبكة في هذا الاختبار: فكّ التشفير يفشل **قبل** أن يُبنى أي كائن Stripe، فالفرع المحروس
    // يُبلَغ فعلاً بلا مفتاح حقيقي ولا اتصال خارجي — وهو ما جعل الاختبار ممكناً قبل TD-52.
    // ============================================================================
    // ========================================================================
    // تحريران متزامنان لمفاتيح الدفع (C12، جواب المالك D-13 = A).
    //
    // كان الصفّ بلا حارس تزامن: مديران يحفظان معاً، فيفوز الأخير صامتاً. والمفاتيحُ ليست حقلَ
    // عرضٍ يُغتفر فيه ذلك — الخاسرُ يظنّ متجره يقبض في حسابٍ وهو يقبض في آخر.
    //
    // ويُفحص هنا أنّ الثاني **يُرفض بـ409** لا أن يُعاد تلقائياً: إعادةُ المحاولة تكتب مفاتيحه
    // فوق مفاتيح الأوّل، وهو العطبُ نفسه بخطوةٍ إضافية.
    // ========================================================================
    [Fact]
    public async Task تحريران_متزامنان_لمفاتيح_الدفع_لا_يفوز_فيهما_الأخير_صامتاً()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var storeAdmin = await storeApi.AdminAsync();

        (await storeAdmin.PutAsJsonAsync("/api/admin/store/payments",
                new { publishableKey = "pk_test_51ConcurrencyFirst", secretKey = "sk_test_51ConcurrencyFirstAaa" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // نسخةٌ قديمة من الصفّ في يد «المدير الثاني»: قرأها قبل أن يكتب الأوّل مرّةً أخرى.
        var stale = await storeApi.WithDbAsync(db => db.StorePaymentAccounts.AsNoTracking().SingleAsync());

        (await storeAdmin.PutAsJsonAsync("/api/admin/store/payments",
                new { publishableKey = "pk_test_51ConcurrencySecond", secretKey = "sk_test_51ConcurrencySecondBb" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // الكتابةُ بالنسخة القديمة تصطدم بالحارس بدل أن تمرّ فوق ما كُتب بعدها.
        var conflict = await storeApi.WithDbAsync(async db =>
        {
            db.Attach(stale);
            stale.Update("pk_test_51ConcurrencyThird", false, "cipher", "…Ccc", null, null);
            try
            {
                await db.SaveChangesAsync();
                return false;
            }
            // النوعُ هو استثناءُ التطبيق لا استثناءُ EF: `AppDbContext` يترجمه، وهو ما يُربط
            // إلى 409 في المعالج العام — أي أنّ ما يُفحص هنا هو ما يصل المستخدم فعلاً.
            catch (ConcurrencyConflictException)
            {
                return true;
            }
        });

        conflict.Should().BeTrue("صفٌّ بلا rowversion كان يقبل هذه الكتابة ويمحو ما قبلها بلا أثر");

        var row = await storeApi.WithDbAsync(db => db.StorePaymentAccounts.AsNoTracking().SingleAsync());
        row.PublishableKey.Should().Be("pk_test_51ConcurrencySecond", "ما كُتب آخراً بنجاح هو ما يبقى");
    }

    [Fact]
    public async Task سرّ_حساب_المتجر_التالف_يُسقط_الدفع_ولا_يرجع_صامتاً_لحساب_النشر()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var storeAdmin = await storeApi.AdminAsync();

        (await storeAdmin.PutAsJsonAsync("/api/admin/store/payments", new
        {
            publishableKey = "pk_test_51CorruptedPublic",
            secretKey = "sk_test_51CorruptedSecretAbc7",
            webhookSecret = "whsec_corrupted_hook",
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // قبل الإفساد: التوجيه يعمل ويعطي مفتاح **المتجر**.
        (await storeApi.Anonymous().GetFromJsonAsync<ConfigBody>("/api/payments/config", TestApi.Json))!
            .PublishableKey.Should().Be("pk_test_51CorruptedPublic");

        // مفتاح النشر في هذه الحزمة فارغ — فهو العلامة التي تكشف الرجوع الصامت لو وقع.
        var deploymentKey = (await _api.Anonymous()
            .GetFromJsonAsync<ConfigBody>("/api/payments/config", TestApi.Json))!.PublishableKey;

        // يُفسَد النصّ المشفّر مع الإبقاء على صيغته: `v1.<keyId>.<base64>` يُحلَّل ثم يفشل فكّه،
        // وهو ما يحدث فعلاً عند تدوير مفتاح أو تلف صفّ — لا نصٌّ عشوائي يُرفض عند التحليل.
        await storeApi.WithDbAsync(async db =>
        {
            var account = await db.StorePaymentAccounts.SingleAsync();
            var parts = account.SecretKeyCipher.Split('.', 3);
            var blob = Convert.FromBase64String(parts[2]);
            blob[^1] ^= 0xFF;   // آخر بايت من النصّ المعمّى: التحقّق من الوسم يفشل
            db.Entry(account).Property(nameof(Souq.Domain.Entities.StorePaymentAccount.SecretKeyCipher))
                .CurrentValue = $"{parts[0]}.{parts[1]}.{Convert.ToBase64String(blob)}";
            return await db.SaveChangesAsync();
        });

        var response = await storeApi.Anonymous().GetAsync("/api/payments/config");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "سرّ لا يُفكّ يوقف الدفع — ولا يُسلّم المتجر إلى حساب النشر بصمت (ADR-0031)");
        (await ProblemAsync(response)).Should().Be((HttpStatusCode.ServiceUnavailable, "PaymentsUnavailable"));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("publishableKey",
            "الاستجابة رفضٌ لا إعداد — ولا تحمل مفتاح أي حساب");
        if (!string.IsNullOrEmpty(deploymentKey))
            body.Should().NotContain(deploymentKey, "وأخصّ ما يجب ألّا يظهر: مفتاح حساب النشر");
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
