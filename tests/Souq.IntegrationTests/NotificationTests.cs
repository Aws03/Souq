using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Notifications;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Identity;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Outbox;
using Souq.Infrastructure.Services;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الإشعارات وصندوق الصادر (المرحلة 14، D-14) عبر HTTP وSQL Server الحقيقيين. معايير الخروج:
//   • مسار الطلب لا ينتظر مزوّد البريد — والمزوّد معطّل تماماً ينجح الطلب، والرسالة تصل من الصادر لاحقاً.
//   • إعادة المحاولة: فشل ⇒ تباعد ثم نجاح؛ آخر محاولة فاشلة ⇒ رسالة ميتة لا تُعاد.
//   • لا رموز ولا بيانات شخصية في السجل (ولا في خطأ الرسالة المخزَّن).
// ومعها: دورة الطلب تُشعر العميل والإدارة ببريد بهوية المتجر، وحفظ خسر سباق rowversion لا يكتب حدثه، ولا بديل طرفي صامت خارج
// التطوير. المُرسِل الخلفي معطّل هنا: كل اختبار يفرغ الصندوق أولاً ثم يشغّل دورته صراحةً.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class NotificationTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public NotificationTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الطلب_لا_ينتظر_مزوّد_البريد_والرسالة_تصل_من_الصادر_بلا_أثر_في_السجل()
    {
        var (_, email) = await _api.NewCustomerAsync();
        await _factory.DispatchNotificationsAsync();

        _factory.Emails.Block();
        try
        {
            var watch = Stopwatch.StartNew();
            (await _api.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "المزوّد معطّل تماماً والطلب لا ينتظره");
            _factory.Emails.LastTo(email, EmailTemplate.PasswordReset).Should().BeNull("لا بريد قبل دورة الإرسال");
        }
        finally
        {
            _factory.Emails.Release();
        }

        await _factory.DispatchNotificationsAsync();
        var token = _factory.Emails.LastResetTokenFor(email);
        (await _api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.PasswordResetTokenHash).SingleAsync()))
            .Should().Be(User.HashToken(token));
        _factory.Logs.Messages.Should().NotContain(m => m.Contains(token) || m.Contains(email));
    }

    [Fact]
    public async Task فشل_المزوّد_يُعاد_بتباعد_حتى_يصل_وبعد_آخر_محاولة_تبقى_ميتة()
    {
        var (_, email) = await _api.NewCustomerAsync();
        await _factory.DispatchNotificationsAsync();
        var userId = await _api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        _factory.Emails.FailNext(1);
        (await _api.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        await _factory.DispatchNotificationsAsync();

        var failed = await LatestResetAsync(userId);
        (failed.Attempts, failed.ProcessedAt, failed.FailedAt).Should().Be((1, (DateTime?)null, (DateTime?)null));
        failed.NextAttemptAt.Should().BeAfter(DateTime.UtcNow.AddSeconds(20), "المحاولة الثانية بعد 30 ثانية");
        failed.LastError.Should().Contain(nameof(EmailDeliveryException)).And.NotContain(email);
        _factory.Emails.LastTo(email, EmailTemplate.PasswordReset).Should().BeNull();
        (await _factory.DispatchNotificationsAsync()).Should().Be(0, "لم يحن وقت المحاولة الثانية بعد");

        await MakeDueAsync(failed.Id);
        await _factory.DispatchNotificationsAsync();
        (await ByIdAsync(failed.Id)).ProcessedAt.Should().NotBeNull();
        _factory.Emails.LastTo(email, EmailTemplate.PasswordReset).Should().NotBeNull();

        // آخر محاولة مسموحة تفشل ⇒ ميتة: لا تُعاد حتى لو "حان وقتها".
        (await _api.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        var last = await LatestResetAsync(userId);
        await _api.WithDbAsync(db => db.OutboxMessages.Where(m => m.Id == last.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Attempts, OutboxRetryPolicy.MaxAttempts - 1)));
        _factory.Emails.FailNext(1);
        await _factory.DispatchNotificationsAsync();

        var dead = await ByIdAsync(last.Id);
        (dead.Attempts, dead.FailedAt is not null, dead.ProcessedAt).Should().Be((OutboxRetryPolicy.MaxAttempts, true, (DateTime?)null));
        await MakeDueAsync(dead.Id);
        (await _factory.DispatchNotificationsAsync()).Should().Be(0, "الميتة لا تُعاد تلقائياً");
    }

    [Fact]
    public async Task دورة_الطلب_تشعر_العميل_والإدارة_وبريدها_بهوية_المتجر_ورابط_التتبّع()
    {
        var created = await _factory.CreateStoreAsync();
        var store = _api.ForStore(created);
        var admin = await store.AdminAsync();
        // اسم صريح كي يُتحقَّق من ظهور السطر نفسه في بريد التأكيد (R-06). حدّ التنبيه الافتراضي 5.
        var productId = await store.CreateProductAsync(admin, price: 20m, stock: 6, name: "سمّاعة الاختبار");
        var (customer, email) = await store.NewCustomerAsync();
        await _factory.DispatchNotificationsAsync();

        var placed = await store.PlaceOrderAsync(customer, productId, 2);   // المتاح 6 ⇒ 4: عبور حدّ التنبيه
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        await _factory.DispatchNotificationsAsync();

        var paid = (await ListAsync(customer)).Items.Should()
            .ContainSingle(n => n.Kind == NotificationKinds.OrderStatus && n.Data["status"] == "Paid").Subject;
        paid.Data["orderId"].Should().Be(orderId.ToString());
        var staff = (await ListAsync(admin)).Items;
        staff.Should().Contain(n => n.Kind == NotificationKinds.NewOrder && n.Data["orderId"] == orderId.ToString());
        staff.Should().Contain(n => n.Kind == NotificationKinds.LowStock && n.Data["productId"] == productId.ToString()
                                    && n.Data["available"] == "4");

        var confirmation = _factory.Emails.LastTo(email, EmailTemplate.OrderConfirmed)!;
        confirmation.FromName.Should().Be(created.Tenant.Name);
        confirmation.Subject.Should().Contain(paid.Data["orderNumber"]);
        // R-06 من طرف إلى طرف: السطر المشترى بكميته وإجماليه، والإجمالي المجمّد (20 × 2) — لا صفر ولا شحن وحده.
        confirmation.TextBody.Should().Contain("سمّاعة الاختبار").And.Contain("40.00");
        confirmation.HtmlBody.Should().Contain("40.00");
        var tracking = new Uri(confirmation.ActionUrl!);
        (tracking.Host, tracking.AbsolutePath.StartsWith("/track/", StringComparison.Ordinal)).Should().Be((created.Host, true));

        (await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Ship", trackingNumber = "JO-77" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _factory.DispatchNotificationsAsync();
        _factory.Emails.LastTo(email, EmailTemplate.OrderShipped)!.TextBody.Should().Contain("JO-77");

        (await UnreadAsync(customer)).Should().Be(2);
        var latest = (await ListAsync(customer)).Items[0];
        latest.Data["status"].Should().Be("Shipped");
        (await customer.PostAsync($"/api/notifications/{latest.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UnreadAsync(customer)).Should().Be(1);
        var (other, _) = await store.NewCustomerAsync();
        (await other.PostAsync($"/api/notifications/{latest.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await customer.PostAsync("/api/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UnreadAsync(customer)).Should().Be(0);
    }

    [Fact]
    public async Task إلغاء_العميل_طلبه_غير_المدفوع_إشعار_بلا_بريد()
    {
        var created = await _factory.CreateStoreAsync();
        var store = _api.ForStore(created);
        var productId = await store.CreateProductAsync(await store.AdminAsync(), stock: 20);
        var (customer, email) = await store.NewCustomerAsync();
        var placed = await store.PlaceOrderAsync(customer, productId, 1);
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;

        (await customer.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = (string?)null })).EnsureSuccessStatusCode();
        await _factory.DispatchNotificationsAsync();

        (await ListAsync(customer)).Items.Should().ContainSingle(n => n.Data["status"] == "Cancelled");
        _factory.Emails.LastTo(email, EmailTemplate.OrderCancelled).Should().BeNull("ألغاه صاحبه قبل الدفع — لا يحتاج بريداً");
    }

    [Fact]
    public async Task حفظ_خسر_سباق_rowversion_لا_يكتب_حدثه_في_الصادر()
    {
        var created = await _factory.CreateStoreAsync();
        var store = _api.ForStore(created);
        var productId = await store.CreateProductAsync(await store.AdminAsync(), stock: 20);
        var (customer, _) = await store.NewCustomerAsync();
        var placed = await store.PlaceOrderAsync(customer, productId, 1);
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;

        await using var first = await _factory.TenantScopeAsync(created.Tenant);
        await using var second = await _factory.TenantScopeAsync(created.Tenant);
        var winnerDb = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var loserDb = second.ServiceProvider.GetRequiredService<AppDbContext>();
        var winner = await winnerDb.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderId);
        var loser = await loserDb.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderId);

        winner.MarkAsPaid(by: OrderActor.PaymentGateway);
        await winnerDb.SaveChangesAsync();
        loser.MarkAsPaid(by: OrderActor.PaymentGateway);
        var lose = () => loserDb.SaveChangesAsync();
        await lose.Should().ThrowAsync<ConcurrencyConflictException>();

        (await _api.WithDbAsync(db => db.OutboxMessages.CountAsync(
                m => m.Type == nameof(Souq.Domain.Events.OrderStatusChanged) && m.Payload.Contains($"\"orderId\":{orderId},"))))
            .Should().Be(1, "حدث الفائز وحده — صفّ الخاسر فُصل مع فشل حفظه");
        loserDb.ChangeTracker.Entries<OutboxMessage>().Should().BeEmpty();
        loser.PendingDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void لا_بديل_طرفي_صامت_خارج_التطوير()
    {
        IConfiguration Config(params (string Key, string Value)[] extra) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=unused;Database=unused",
                ["Jwt:Key"] = "integration-tests-only-signing-key-0123456789abcdef0123456789",
                ["Jwt:Issuer"] = "Souq", ["Jwt:Audience"] = "SouqClient",
                ["Payments:Provider"] = "Fake",
            }.Concat(extra.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value))))
            .Build();
        var production = new HostingEnvironment { EnvironmentName = "Production", ApplicationName = "Souq.API", ContentRootPath = AppContext.BaseDirectory };

        var silent = () => new ServiceCollection().AddInfrastructure(Config(), production);
        silent.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Email:Provider=Log");

        var services = new ServiceCollection().AddInfrastructure(Config(("Email:Provider", "Log")), production);
        services.Single(d => d.ServiceType == typeof(IEmailSender)).ImplementationType.Should().Be<ConsoleEmailService>();
    }

    // ════════════════════════════════════════════════════════════════════════
    // مسارات التعافي الثلاثة في صندوق الصادر (TD-34، M14).
    //
    // كانت مؤكَّدةً **بالقراءة وحدها**: الحذف الدوري وعقد الإيجار وسباق مُرسِلَين كُتبت مع المسار السعيد ولم
    // يفحص أيُّها اختبار. وهذه بالضبط الأماكن التي لا يكشف خطأها إلا الإنتاج: حذفٌ يأخذ صفّاً لم يُرسَل،
    // أو عقدٌ لا ينتهي فيبقى صفٌّ عالقاً إلى الأبد، أو مُرسِلان يُرسلان للزبون رسالتين.
    // ════════════════════════════════════════════════════════════════════════

    // ========================================================================
    // 1) الحذف الدوري. الخطر فيه ليس ما يحذفه بل ما قد يحذفه بالخطأ: جملة `DELETE` بشرطٍ ناقص تأخذ معها
    // رسالةً لم تُرسَل بعد (فلا تصل أبداً) أو رسالةً ميتة (فيضيع سبب فشلها وهو كلّ قيمتها).
    //
    // فالحالات الأربع تُبنى صراحةً وتُحذف دفعةً واحدة، ويُفحص الباقي لا المحذوف وحده.
    // ========================================================================
    [Fact]
    public async Task الحذف_الدوري_يأخذ_المُنجز_القديم_وحده_ويترك_المنتظر_والميت_والحديث()
    {
        var tenant = await _api.TenantAsync();
        var now = DateTime.UtcNow;
        var retention = now.AddDays(-14);

        var oldDone = await SeedOutboxAsync(tenant.Id, processedAt: now.AddDays(-30));
        var freshDone = await SeedOutboxAsync(tenant.Id, processedAt: now.AddHours(-1));
        var pending = await SeedOutboxAsync(tenant.Id);
        var dead = await SeedOutboxAsync(tenant.Id, failedAt: now.AddDays(-30));

        await using var scope = _factory.Services.CreateAsyncScope();
        var deleted = await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>()
            .PurgeProcessedAsync(retention, CancellationToken.None);

        deleted.Should().BeGreaterThanOrEqualTo(1);
        (await ExistsAsync(oldDone)).Should().BeFalse("منجزٌ أقدم من مدّة الاحتفاظ");
        (await ExistsAsync(freshDone)).Should().BeTrue("منجزٌ أحدث من مدّة الاحتفاظ");
        (await ExistsAsync(pending)).Should().BeTrue("لم يُرسَل بعد — حذفه يعني رسالة لا تصل أبداً");
        (await ExistsAsync(dead)).Should().BeTrue("الميت يبقى للتشخيص مهما قدُم، وهو ما يقوله NotificationSettings");
    }

    // ========================================================================
    // 2) عقد الإيجار، بنصفيه — والثاني هو المهمّ:
    //   • عقدٌ سارٍ يحمي: نسخةٌ ثانية من الخادم لا تلتقط رسالةً تُعالَج الآن.
    //   • وعقدٌ منتهٍ **يُفرج**: عمليةٌ ماتت وهي ممسكة برسالة لا يجوز أن تُجمِّدها إلى الأبد. هذا هو ما
    //     يجعل التسليم "مرّة على الأقل" صحيحاً، وهو النصف الذي لا يظهر خطؤه إلا برسالة لا تصل أبداً.
    // ========================================================================
    [Fact]
    public async Task العقد_السارِي_يحمي_الرسالة_والعقد_المنتهي_يُفرج_عنها()
    {
        var (_, email) = await _api.NewCustomerAsync();
        await _factory.DispatchNotificationsAsync();
        var userId = await _api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        (await _api.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        var message = await LatestResetAsync(userId);

        // عقدٌ سارٍ (كأنّ نسخةً أخرى تُعالجها الآن): لا تُلتقط.
        await SetLockAsync(message.Id, DateTime.UtcNow.AddMinutes(2));
        await _factory.DispatchNotificationsAsync();

        (await ByIdAsync(message.Id)).ProcessedAt.Should().BeNull("عقدٌ سارٍ يعني أنّ غيرها يعالجها");
        _factory.Emails.CountTo(email, EmailTemplate.PasswordReset).Should().Be(0);

        // انتهى العقد (ماتت العملية الممسكة بها): تُستعاد وتُرسَل.
        await SetLockAsync(message.Id, DateTime.UtcNow.AddSeconds(-1));
        await _factory.DispatchNotificationsAsync();

        var recovered = await ByIdAsync(message.Id);
        recovered.ProcessedAt.Should().NotBeNull("العقد المنتهي يُفرج عن الرسالة بدل أن تعلق إلى الأبد");
        recovered.LockedUntil.Should().BeNull("النجاح يمسح العقد");
        recovered.Attempts.Should().Be(0, "لم تفشل — انتظرت فقط");
        _factory.Emails.CountTo(email, EmailTemplate.PasswordReset).Should().Be(1);
    }

    // ========================================================================
    // 3) سباق مُرسِلَين — والسباق هنا **حقيقي لا محاكى**: مُرسِلٌ أول يدخل المزوّد ويتعطّل فيه (Block)، وهو
    // ممسكٌ بالعقد فعلاً، ويجري الثاني في تلك الأثناء على القاعدة نفسها. فالنافذة التي يفشل فيها الحجز
    // مفتوحةٌ على مصراعيها، لا لحظةً يُرجى أن تُصادف.
    //
    // والدعوى المُختبَرة دعوى منتج لا آلية: **الزبون يتلقّى رسالةً واحدة**. عدّها لا قراءة آخرها، فرسالتان
    // متطابقتان تُقرأان واحدةً.
    // ========================================================================
    [Fact]
    public async Task مُرسِلان_معاً_لا_يُرسلان_للزبون_رسالتين()
    {
        var (_, email) = await _api.NewCustomerAsync();
        await _factory.DispatchNotificationsAsync();
        var userId = await _api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        (await _api.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        var message = await LatestResetAsync(userId);

        _factory.Emails.Block();
        Task<int> first;
        try
        {
            first = Task.Run(() => _factory.DispatchNotificationsAsync());

            // لا يبدأ الثاني قبل أن يُمسك الأول العقد فعلاً — وإلا صار الاختبار سباقاً على الصدفة.
            await Wait.UntilAsync(async () => (await ByIdAsync(message.Id)).LockedUntil is not null,
                "لم يحجز المُرسِل الأول الرسالة");

            var second = await _factory.DispatchNotificationsAsync();
            second.Should().Be(0, "الرسالة الوحيدة المستحقّة محجوزة، فلا شيء للثاني");
            (await ByIdAsync(message.Id)).ProcessedAt.Should().BeNull("الأول ما زال داخل المزوّد");
        }
        finally
        {
            _factory.Emails.Release();
        }

        (await first).Should().Be(1);
        var done = await ByIdAsync(message.Id);
        done.ProcessedAt.Should().NotBeNull();
        done.Attempts.Should().Be(0, "لم يفشل شيء — الثاني لم يلمسها أصلاً");
        _factory.Emails.CountTo(email, EmailTemplate.PasswordReset)
            .Should().Be(1, "مُرسِلان لا يعنيان رسالتين للزبون");
    }

    // صفّ صادر بحالةٍ معلومة، بلا مرور بمسار المنتج: هذه اختبارات **الصندوق** لا مُنتِجي الرسائل، وبناء
    // أربع حالات من أحداث حقيقية كان سيخلط ما يُفحص بما يُهيّأ.
    private async Task<long> SeedOutboxAsync(int tenantId, DateTime? processedAt = null, DateTime? failedAt = null)
    {
        var id = await _api.WithDbAsync(async db =>
        {
            var message = OutboxMessage.For(
                // مستخدمٌ غير موجود عمداً: هذا الصفّ يُحذف أو يبقى، ولا يُعالَج أبداً في هذا الاختبار.
                new PasswordResetRequested(0, "https://outbox-seed.example.test"),
                tenantId, DateTime.UtcNow.AddDays(-40));
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
            return message.Id;
        });

        if (processedAt is not null || failedAt is not null)
            await _api.WithDbAsync(db => db.OutboxMessages.Where(m => m.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.ProcessedAt, processedAt)
                    .SetProperty(m => m.FailedAt, failedAt)));
        return id;
    }

    private Task<bool> ExistsAsync(long id) =>
        _api.WithDbAsync(db => db.OutboxMessages.AnyAsync(m => m.Id == id));

    private Task<int> SetLockAsync(long id, DateTime until) =>
        _api.WithDbAsync(db => db.OutboxMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedUntil, until)));

    private Task<OutboxMessage> LatestResetAsync(int userId) =>
        _api.WithDbAsync(db => db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == nameof(PasswordResetRequested) && m.Payload.Contains($"\"userId\":{userId},"))
            .OrderByDescending(m => m.Id).FirstAsync());

    private Task<OutboxMessage> ByIdAsync(long id) =>
        _api.WithDbAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id));

    private Task<int> MakeDueAsync(long id) =>
        _api.WithDbAsync(db => db.OutboxMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1))));

    private static async Task<TestApi.PageBody<NotificationBody>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<TestApi.PageBody<NotificationBody>>("/api/notifications?pageSize=50", TestApi.Json))!;

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CountBody>("/api/notifications/unread-count", TestApi.Json))!.Count;

    private sealed record NotificationBody(int Id, string Kind, Dictionary<string, string> Data, bool IsRead);
    private sealed record CountBody(int Count);
}
