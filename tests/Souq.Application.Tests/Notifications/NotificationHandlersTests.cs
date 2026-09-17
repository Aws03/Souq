using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Security;
using Souq.Application.Features.Notifications;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Events;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Notifications;

// صندوق الصادر (المرحلة 14): سياسة إعادة المحاولة، وأنواع الرسائل المسموحة، ومعالجو الرسائل — الرمز يُولَّد عند الإرسال وتجزئته
// تُحفظ قبله، والحساب المتغيّر منذ الطلب لا تصله رسالة، وأحداث الطلب والمخزون تصير إشعارات للعميل والإدارة وبريداً لما يستحقّه.
public class OutboxPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void تباعد_متصاعد_ثم_رسالة_ميتة_عند_الحدّ()
    {
        OutboxRetryPolicy.NextAttemptAt(1, Now).Should().Be(Now.AddSeconds(30));
        OutboxRetryPolicy.NextAttemptAt(2, Now).Should().Be(Now.AddMinutes(2));
        OutboxRetryPolicy.NextAttemptAt(OutboxRetryPolicy.MaxAttempts - 1, Now).Should().Be(Now.AddHours(6));
        OutboxRetryPolicy.NextAttemptAt(OutboxRetryPolicy.MaxAttempts, Now).Should().BeNull();
        var noFailure = () => OutboxRetryPolicy.NextAttemptAt(0, Now);
        noFailure.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void الأنواع_المسجّلة_وحدها_تُخزَّن_وتُقرأ_والحالات_بأسمائها()
    {
        var change = new OrderStatusChanged(9, 3, OrderStatus.Pending, OrderStatus.Paid, OrderActorKind.PaymentGateway);
        var payload = NotificationMessageTypes.Serialize(change);

        payload.Should().Contain("\"Paid\"").And.NotContain("\"to\":1");
        NotificationMessageTypes.Deserialize(NotificationMessageTypes.NameOf(typeof(OrderStatusChanged)), payload).Should().Be(change);
        NotificationMessageTypes.Deserialize("System.Diagnostics.Process", "{}").Should().BeNull();
        var unregistered = () => NotificationMessageTypes.NameOf(typeof(string));
        unregistered.Should().Throw<InvalidOperationException>();
    }
}

public class IdentityEmailHandlersTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IEmailComposer _composer = Substitute.For<IEmailComposer>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly FixedClock _clock = new();
    private EmailMessage? _sent;
    private EmailContent? _composed;

    public IdentityEmailHandlersTests()
    {
        _tenants.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Tenant("متجر النخبة", "elite", "JOD", "ar", "Asia/Amman"));
        _composer.Compose(Arg.Do<EmailContent>(c => _composed = c)).Returns(new ComposedEmail("عنوان", "<p>html</p>", "text"));
        _sender.SendAsync(Arg.Do<EmailMessage>(m => _sent = m), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private NotificationEmails Emails() => new(_tenants, TestTenant.Context(id: 1), _composer, _sender);

    private static User SavedUser(int id, string email = "buyer@souq.test")
    {
        var user = new User("مشترٍ", email, "$2a$hash", Roles.Customer);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(user, id);
        return user;
    }

    private static string TokenIn(string link) =>
        Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);

    [Fact]
    public async Task رابط_إعادة_التعيين_يُولَّد_عند_الإرسال_وتجزئته_تُحفظ_قبله_بهوية_المتجر()
    {
        var user = SavedUser(4);
        _users.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(user);

        await new PasswordResetEmailHandler(_users, _uow, Emails(), _clock)
            .HandleAsync(new PasswordResetRequested(4, "https://elite.test"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        });
        _sent!.To.Should().Be("buyer@souq.test");
        _sent.ActionUrl.Should().StartWith("https://elite.test/reset-password?token=");
        user.PasswordResetTokenHash.Should().Be(User.HashToken(TokenIn(_sent.ActionUrl!)));
        (_sent.FromName, _sent.Kind).Should().Be(("متجر النخبة", nameof(EmailTemplate.PasswordReset)));
        (_composed!.Template, _composed.Culture, _composed.Branding.StoreName).Should().Be((EmailTemplate.PasswordReset, "ar", "متجر النخبة"));
    }

    [Fact]
    public async Task حساب_تغيّر_منذ_الطلب_لا_تصله_رسالة()
    {
        var disabled = SavedUser(4);
        disabled.Disable();
        var confirmed = SavedUser(5);   // له كلمة مرور (قبل دعوته) وأكّد بريده منذ الطلب
        confirmed.GenerateEmailVerificationToken(_clock.UtcNow);
        confirmed.ConfirmEmail(_clock.UtcNow);
        _users.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(disabled);
        _users.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(confirmed);
        _uow.ClearReceivedCalls();

        await new PasswordResetEmailHandler(_users, _uow, Emails(), _clock)
            .HandleAsync(new PasswordResetRequested(4, "https://elite.test"), CancellationToken.None);
        await new PasswordResetEmailHandler(_users, _uow, Emails(), _clock)
            .HandleAsync(new PasswordResetRequested(404, "https://elite.test"), CancellationToken.None);
        await new EmailVerificationEmailHandler(_users, _uow, Emails(), _clock)
            .HandleAsync(new EmailVerificationRequested(5, "https://elite.test"), CancellationToken.None);
        await new InvitationEmailHandler(_users, _uow, Emails(), _clock)
            .HandleAsync(new AccountInvited(5, "متجر", "https://elite.test"), CancellationToken.None);

        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الدعوة_المعلّقة_تُجدَّد_عند_الإرسال_باسم_الجهة_الداعية()
    {
        var (invited, oldToken) = User.Invite("موظّف", "clerk@elite.test", Roles.TenantStaff, _clock.UtcNow);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(invited, 8);
        _users.GetByIdAsync(8, Arg.Any<CancellationToken>()).Returns(invited);

        await new InvitationEmailHandler(_users, _uow, Emails(), _clock)
            .HandleAsync(new AccountInvited(8, "متجر النخبة", "https://elite.test"), CancellationToken.None);

        _sent!.ActionUrl.Should().StartWith("https://elite.test/accept-invitation?token=");
        TokenIn(_sent.ActionUrl!).Should().NotBe(oldToken);
        invited.PasswordResetTokenHash.Should().Be(User.HashToken(TokenIn(_sent.ActionUrl!)));
        _composed!.Values["inviter"].Should().Be("متجر النخبة");
    }

    [Fact]
    public async Task فشل_المزوّد_يصل_للمُرسِل_ليعيد_المحاولة()
    {
        _users.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(SavedUser(4));
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new EmailDeliveryException("503")));

        var act = () => new EmailVerificationEmailHandler(_users, _uow, Emails(), _clock)
            .HandleAsync(new EmailVerificationRequested(4, "https://elite.test"), CancellationToken.None);

        await act.Should().ThrowAsync<EmailDeliveryException>();
    }
}

public class OrderNotificationHandlersTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly INotificationOutbox _outbox = Substitute.For<INotificationOutbox>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<Notification> _added = [];
    private readonly Order _order = TestCatalog.WithId(new Order(customerId: 3, "عمّان", "JOD"), 9);
    private readonly Customer _customer = TestCatalog.WithId(new Customer(userId: 7, "سارة", "sara@souq.test"), 3);

    public OrderNotificationHandlersTests()
    {
        _order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        _order.AssignNumber(1001);
        _order.Place(new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));   // الإجماليات المجمّدة مصدر البريد (R-06)
        _orders.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(_order);
        _orders.GetWithItemsAsync(9, Arg.Any<CancellationToken>()).Returns(_order);
        _customers.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(_customer);
        _users.ListActiveIdsByRolesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([20, 21]);
        _notifications.AddAsync(Arg.Do<Notification>(_added.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private OrderStatusChangedHandler Handler() => new(_orders, _customers, _users, _notifications, _outbox, _uow);

    private static OrderStatusChanged Change(OrderStatus from, OrderStatus to, OrderActorKind by) => new(9, 3, from, to, by);

    [Fact]
    public async Task الدفع_يشعر_العميل_والإدارة_ويطلب_بريد_التأكيد_في_حفظ_واحد()
    {
        await Handler().HandleAsync(Change(OrderStatus.Pending, OrderStatus.Paid, OrderActorKind.PaymentGateway), CancellationToken.None);

        _added.Select(n => (n.RecipientUserId, n.Kind)).Should().Equal(
            (7, NotificationKinds.OrderStatus), (20, NotificationKinds.NewOrder), (21, NotificationKinds.NewOrder));
        _added[0].Data.Should().Contain("\"orderNumber\":\"1001\"").And.Contain("\"status\":\"Paid\"");
        await _users.Received(1).ListActiveIdsByRolesAsync(
            Arg.Is<IReadOnlyCollection<string>>(r => r.SequenceEqual(new[] { Roles.TenantAdmin, Roles.TenantStaff })), Arg.Any<CancellationToken>());
        _outbox.Received(1).Enqueue(new OrderEmailRequested(9, OrderStatus.Paid));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderActorKind.Customer, false)]      // العميل ألغى طلبه غير المدفوع
    [InlineData(OrderStatus.Pending, OrderActorKind.System, false)]        // انتهت مهلة الدفع
    [InlineData(OrderStatus.Pending, OrderActorKind.Staff, true)]          // ألغاه المتجر
    [InlineData(OrderStatus.Paid, OrderActorKind.Staff, true)]             // مدفوع ⇒ استرداد
    public async Task الإلغاء_يشعر_العميل_وبريده_لما_يهمّه_وحده(OrderStatus from, OrderActorKind by, bool email)
    {
        await Handler().HandleAsync(Change(from, OrderStatus.Cancelled, by), CancellationToken.None);

        _added.Should().ContainSingle(n => n.RecipientUserId == 7 && n.Kind == NotificationKinds.OrderStatus);
        if (email) _outbox.Received(1).Enqueue(new OrderEmailRequested(9, OrderStatus.Cancelled));
        else _outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task عميل_ممحوّ_لا_إشعار_له_ولا_بريد_والإدارة_تعلم_بالطلب()
    {
        _customer.Erase(DateTime.UtcNow);

        await Handler().HandleAsync(Change(OrderStatus.Pending, OrderStatus.Paid, OrderActorKind.PaymentGateway), CancellationToken.None);

        _added.Should().OnlyContain(n => n.Kind == NotificationKinds.NewOrder).And.HaveCount(2);
        _outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task نفاد_وشيك_يشعر_من_يرى_المخزون_باسم_المنتج()
    {
        var products = Substitute.For<IProductRepository>();
        products.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(TestCatalog.Product("سماعات لاسلكية", id: 4));

        await new StockBecameLowHandler(products, _users, _notifications, TestTenant.Context(), _uow)
            .HandleAsync(new StockBecameLow(4, 4, 3, 5), CancellationToken.None);

        _added.Should().HaveCount(2).And.OnlyContain(n => n.Kind == NotificationKinds.LowStock);
        _added[0].Data.Should().Contain("سماعات لاسلكية").And.Contain("\"available\":\"3\"");
        await _users.Received(1).ListActiveIdsByRolesAsync(
            Arg.Is<IReadOnlyCollection<string>>(r => r.SequenceEqual(RolePermissions.RolesGranting(Permissions.Inventory.View))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task إشعار_المخزون_المنخفض_يسمّي_المتغيّر_بوصفه_لمنتج_بخيارات()
    {
        var product = TestCatalog.Product("قميص", id: 4);
        var large = TestCatalog.WithId(TestCatalog.AddVariant(product, new Souq.Domain.ValueObjects.Money(25, "JOD")), 41);
        var products = Substitute.For<IProductRepository>();
        products.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(product);

        await new StockBecameLowHandler(products, _users, _notifications, TestTenant.Context(), _uow)
            .HandleAsync(new StockBecameLow(4, large.Id, 1, 5), CancellationToken.None);

        _added[0].Data.Should().Contain("\"variantLabel\":\"L\"").And.Contain("\"variantId\":\"41\"").And.Contain("قميص");
    }

    [Fact]
    public async Task إشعار_منتج_بسيط_بلا_وصف_متغيّر()
    {
        var products = Substitute.For<IProductRepository>();
        products.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(TestCatalog.Product("سماعات", id: 4));

        await new StockBecameLowHandler(products, _users, _notifications, TestTenant.Context(), _uow)
            .HandleAsync(new StockBecameLow(4, 4, 3, 5), CancellationToken.None);

        _added[0].Data.Should().NotContain("variantLabel");
    }

    [Fact]
    public async Task بريد_الطلب_بقالب_حالته_ورابط_التتبّع_على_نطاق_المتجر()
    {
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Tenant("متجر النخبة", "elite", "JOD", "ar", "Asia/Amman"));
        var composer = Substitute.For<IEmailComposer>();
        EmailContent? composed = null;
        composer.Compose(Arg.Do<EmailContent>(c => composed = c)).Returns(new ComposedEmail("s", "h", "t"));
        var sender = Substitute.For<IEmailSender>();
        var origins = Substitute.For<IStoreOrigins>();
        origins.ForStoreAsync(1, Arg.Any<CancellationToken>()).Returns("https://elite.test");
        var emails = new NotificationEmails(tenants, TestTenant.Context(id: 1), composer, sender);

        await new OrderEmailHandler(_orders, _customers, origins, TestTenant.Context(id: 1), emails)
            .HandleAsync(new OrderEmailRequested(9, OrderStatus.Paid), CancellationToken.None);

        composed!.Template.Should().Be(EmailTemplate.OrderConfirmed);
        composed.ActionUrl.Should().Be($"https://elite.test/track/{_order.TrackingToken}");
        (composed.Values["orderNumber"], composed.Values["total"], composed.Values["currency"]).Should().Be(("1001", "50.00", "JOD"));
        await sender.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m.To == "sara@souq.test"), Arg.Any<CancellationToken>());
    }

    // R-06: المعالج كان يحمّل الجذر بلا أسطره (GetByIdAsync = FindAsync بلا Include)، فيُجمع الفرعي من صفر سطر —
    // بريد بقيمة الشحن وحدها، وطلب بخصم يرمي InvalidMoneyException (خصم أكبر من فرعي صفر) فتموت رسالته بعد آخر محاولة.
    [Fact]
    public async Task بريد_الطلب_يحمل_أسطره_وإجمالياته_المجمّدة_لا_محسوبة_من_تجمّع_ناقص()
    {
        var order = TestCatalog.WithId(new Order(customerId: 3, "عمّان", "JOD"), 11);
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 2);
        order.ApplyCoupon("SAVE10", new Money(10, "JOD"));
        order.ApplyShipping("توصيل", new Money(5, "JOD"), "Aramex", null, 2, 4, "JO");
        order.AssignNumber(1042);
        order.Place(new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));
        _orders.GetWithItemsAsync(11, Arg.Any<CancellationToken>()).Returns(order);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Tenant("متجر النخبة", "elite", "JOD", "ar", "Asia/Amman"));
        var composer = Substitute.For<IEmailComposer>();
        EmailContent? composed = null;
        composer.Compose(Arg.Do<EmailContent>(c => composed = c)).Returns(new ComposedEmail("s", "h", "t"));
        var origins = Substitute.For<IStoreOrigins>();
        origins.ForStoreAsync(1, Arg.Any<CancellationToken>()).Returns("https://elite.test");
        var emails = new NotificationEmails(tenants, TestTenant.Context(id: 1), composer, Substitute.For<IEmailSender>());

        await new OrderEmailHandler(_orders, _customers, origins, TestTenant.Context(id: 1), emails)
            .HandleAsync(new OrderEmailRequested(11, OrderStatus.Paid), CancellationToken.None);

        // كل سطر بكميته وإجمالي سطره كما جُمّد على الطلب.
        composed!.Lines.Should().ContainSingle().Which.Should().Be(new EmailLine("سماعات", 2, "100.00"));
        // 100 فرعي − 10 خصم + 5 شحن = 95 — من الأعمدة المثبَّتة لا من حساب لحظة الإرسال.
        (composed.Values["subtotal"], composed.Values["discount"], composed.Values["shipping"], composed.Values["total"])
            .Should().Be(("100.00", "10.00", "5.00", "95.00"));
        composed.Values["orderNumber"].Should().Be("1042");
    }

    [Fact]
    public async Task بريد_طلب_بلا_خصم_ولا_شحن_لا_يعرض_سطريهما()
    {
        var composer = Substitute.For<IEmailComposer>();
        EmailContent? composed = null;
        composer.Compose(Arg.Do<EmailContent>(c => composed = c)).Returns(new ComposedEmail("s", "h", "t"));
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Tenant("متجر النخبة", "elite", "JOD", "ar", "Asia/Amman"));
        var origins = Substitute.For<IStoreOrigins>();
        origins.ForStoreAsync(1, Arg.Any<CancellationToken>()).Returns("https://elite.test");
        var emails = new NotificationEmails(tenants, TestTenant.Context(id: 1), composer, Substitute.For<IEmailSender>());

        await new OrderEmailHandler(_orders, _customers, origins, TestTenant.Context(id: 1), emails)
            .HandleAsync(new OrderEmailRequested(9, OrderStatus.Paid), CancellationToken.None);

        composed!.Values.Should().NotContainKey("discount").And.NotContainKey("shipping");
        composed.Values["subtotal"].Should().Be("50.00");
    }
}

public class NotificationUseCasesTests
{
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = new();

    [Fact]
    public async Task الحساب_الحالي_يعلّم_إشعاره_وإشعار_غيره_404()
    {
        var mine = new Notification(7, NotificationKinds.OrderStatus, "{}");
        _notifications.FindForRecipientAsync(1, 7, Arg.Any<CancellationToken>()).Returns(mine);
        var handler = new MarkNotificationReadHandler(_notifications, TestCurrentUser.Customer(7), _uow, _clock);

        (await handler.Handle(new MarkNotificationReadCommand(1), CancellationToken.None)).IsSuccess.Should().BeTrue();
        mine.ReadAt.Should().Be(_clock.UtcNow);

        (await handler.Handle(new MarkNotificationReadCommand(2), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تعليم_الكلّ_لحساب_المتصل_وحده()
    {
        await new MarkAllNotificationsReadHandler(_notifications, TestCurrentUser.Staff(901), _clock)
            .Handle(new MarkAllNotificationsReadCommand(), CancellationToken.None);

        await _notifications.Received(1).MarkAllReadAsync(901, _clock.UtcNow, Arg.Any<CancellationToken>());
    }
}
