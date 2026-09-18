using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Enums;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// المخزون والطلبات على SQL Server الحقيقي (المرحلة 6، ADR-0026): الطلب يحجز، الدفع يلتزم (هنا وحده يُسجَّل البيع)،
// الإلغاء يحرّر أو يعيد، ومنسّق المهلة يلتقط المهجور. آخر قطعة تُباع مرّة واحدة تحت التزامن، والسجلّ يطابق الموجود
// في كل خطوة، والتصحيح بفارق لا يمحو بيعاً (C4). ودقّة الدينار بثلاث خانات.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class InventoryAndOrderTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public InventoryAndOrderTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task نسختان_تحجزان_آخر_وحدة_والقاعدة_ترفض_الثانية()
    {
        // Phase 0 C1 على مستوى المخزون: نسختان في الذاكرة رأتا متاحاً 1 وحجزتا معاً — rowversion يرفض الحفظ الثاني،
        // ونقطة الحفظ تُسقط حجزه معه فلا يبقى حجز يتيم.
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 1);
        await using var firstScope = await _factory.TenantScopeAsync();
        await using var secondScope = await _factory.TenantScopeAsync();
        var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstCopy = await first.InventoryItems.SingleAsync(i => i.ProductId == productId);
        var secondCopy = await second.InventoryItems.SingleAsync(i => i.ProductId == productId);
        first.StockReservations.Add(firstCopy.Reserve("it:first", 1, DateTime.UtcNow.AddMinutes(5), "x"));
        second.StockReservations.Add(secondCopy.Reserve("it:second", 1, DateTime.UtcNow.AddMinutes(5), "x"));

        await first.SaveChangesAsync();
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await AssertReconciledAsync(productId, onHand: 1, reserved: 1);
    }

    [Fact]
    public async Task طلبات_متوازية_على_آخر_قطعة_تبيعها_مرّة_واحدة_والبقية_نفاد()
    {
        // معيار خروج المرحلة 6: خمسة عملاء على آخر قطعة معاً ⇒ طلب واحد بالضبط، والبقية 422 برمز واضح — لا 409 عابر
        // (تعارض التزامن يُعاد من قراءة جديدة) ولا بيع زائد.
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 1);
        var customers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _api.NewCustomerAsync()));

        var responses = await Task.WhenAll(customers.Select(c => _api.PlaceOrderAsync(c.Client, productId, 1)));

        var winner = Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.Created);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            (await ProblemAsync(rejected)).Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));
        await AssertReconciledAsync(productId, onHand: 1, reserved: 1);

        // الدفع يلتزم الحجز: الآن فقط تخرج القطعة من الموجود ويُسجَّل البيع.
        var orderId = (await responses[winner].Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await customers[winner].Client.PostAsync($"/api/orders/{orderId}/confirm-payment", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertReconciledAsync(productId, onHand: 0, reserved: 0);
        (await LedgerOf(productId)).Count(m => m.Type == StockMovementType.Sale).Should().Be(1);
    }

    [Fact]
    public async Task الالتزام_والتحرير_على_الحجز_نفسه_يتسابقان_فينجح_واحد_ولا_ينزل_المخزون_تحت_الصفر()
    {
        // السباق الذي يوجد فعلاً في التشغيل: الدفع يُلتزم الحجز في اللحظة التي يحرّره فيها منسّق انتهاء
        // المهلة. الاثنان يمسّان Reserved، والالتزام يمسّ OnHand أيضاً — فلو نجحا معاً لخرجت الوحدة مرّتين:
        // بيعاً وإرجاعاً، والدفاتر لا تقول أيّهما. الاختبار أعلاه يسابق حجزَين؛ هذا يسابق **نهايتَي** حجز
        // واحد، وهو ما لا يغطّيه أي اختبار آخر (M7).
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 1);

        // حجز واحد قائم، محفوظ فعلاً.
        int reservationId;
        await using (var setup = await _factory.TenantScopeAsync())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.InventoryItems.SingleAsync(i => i.ProductId == productId);
            var reservation = item.Reserve("it:race", 1, DateTime.UtcNow.AddMinutes(5), "سباق");
            db.StockReservations.Add(reservation);
            await db.SaveChangesAsync();
            reservationId = reservation.Id;
        }
        await AssertReconciledAsync(productId, onHand: 1, reserved: 1);

        // نسختان في الذاكرة، كلٌّ ترى الحجز نشطاً: واحدة تلتزمه (دفع نجح) والأخرى تحرّره (انتهت المهلة).
        await using var commitScope = await _factory.TenantScopeAsync();
        await using var releaseScope = await _factory.TenantScopeAsync();
        var committing = commitScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var releasing = releaseScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var commitItem = await committing.InventoryItems.SingleAsync(i => i.ProductId == productId);
        var commitReservation = await committing.StockReservations.SingleAsync(r => r.Id == reservationId);
        var releaseItem = await releasing.InventoryItems.SingleAsync(i => i.ProductId == productId);
        var releaseReservation = await releasing.StockReservations.SingleAsync(r => r.Id == reservationId);

        var sale = commitItem.Commit(commitReservation, DateTime.UtcNow);
        if (sale is not null) committing.StockMovements.Add(sale);
        // Release هو مسار انتهاء المهلة (حجز لم يُلتزم)؛ وهو يرى نسخته الخاصّة من الحجز نشطةً فينقص المحجوز.
        releaseItem.Release(releaseReservation, DateTime.UtcNow, expired: true).Should().BeTrue(
            "النسخة القديمة ما زالت ترى الحجز نشطاً — وهذا هو السباق بعينه");

        await committing.SaveChangesAsync();
        var act = () => releasing.SaveChangesAsync();

        // rowversion على صفّ المخزون يرفض الثانية — والوحدة خرجت بيعاً واحداً لا مرّتين، ولا محجوزاً سالباً.
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await AssertReconciledAsync(productId, onHand: 0, reserved: 0);
        (await LedgerOf(productId)).Count(m => m.Type == StockMovementType.Sale)
            .Should().Be(1, "بيع واحد بالضبط: الالتزام ربح والتحرير خسر بلا أثر");
    }


    [Fact]
    public async Task طلبات_متوازية_لا_تحجز_أكثر_من_المخزون_أبداً()
    {
        const int initialStock = 3;
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: initialStock);
        var customers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _api.NewCustomerAsync()));

        var responses = await Task.WhenAll(customers.Select(c => _api.PlaceOrderAsync(c.Client, productId, 1)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(initialStock);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            (await ProblemAsync(rejected)).Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));
        await AssertReconciledAsync(productId, onHand: initialStock, reserved: initialStock);
    }

    [Fact]
    public async Task دفعة_متزامنة_بكميّات_مختلفة_لا_تتجاوز_المخزون()
    {
        // الاختباران أعلاه يطلبان قطعة واحدة لكل عميل، فعدد الفائزين يساوي المخزون دائماً ولا يُختبَر إلا
        // "الكلّ أو لا شيء". بكميّات مختلفة يصير السؤال أصعب: أي تركيبة من الفائزين مقبولة ما دام مجموعها لا
        // يتجاوز المخزون — وهو ما يكشف احتساباً جزئياً خاطئاً لا يظهر عند الكمية 1 (M5).
        const int initialStock = 6;
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: initialStock);
        int[] quantities = [3, 2, 4, 1, 2, 3, 1, 5, 2, 1];
        var customers = await Task.WhenAll(quantities.Select(_ => _api.NewCustomerAsync()));

        var responses = await Task.WhenAll(
            customers.Select((c, i) => _api.PlaceOrderAsync(c.Client, productId, quantities[i])));

        // الثابت الحقيقي: مجموع ما نجح لا يتجاوز المخزون إطلاقاً — لا عدد الفائزين، فهو غير حتمي بحقّ.
        var reserved = responses.Select((r, i) => r.StatusCode == HttpStatusCode.Created ? quantities[i] : 0).Sum();
        reserved.Should().BeLessThanOrEqualTo(initialStock, "لا بيع زائد مهما كان ترتيب الوصول");
        reserved.Should().BeGreaterThan(0, "دفعة كاملة مرفوضة تعني قفلاً لا تزامناً");

        // وكل رفض يقول سببه برمزه: 422 InsufficientStock — لا 409 تعارض تزامن يتسرّب إلى العميل.
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            (await ProblemAsync(rejected)).Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));

        // والدفاتر تطابق ما جرى بالضبط: المحجوز هو مجموع الفائزين، والموجود لم يتغيّر (الحجز ليس بيعاً).
        await AssertReconciledAsync(productId, onHand: initialStock, reserved: reserved);
    }

    [Fact]
    public async Task السجلّ_يطابق_الموجود_عبر_دورة_الطلب_كاملة()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        await AssertReconciledAsync(productId, onHand: 5, reserved: 0);

        var paid = await PlaceAsync(customer, productId, 2);                       // حجز: المتاح 3، السجلّ كما هو
        await AssertReconciledAsync(productId, onHand: 5, reserved: 2);

        (await customer.PostAsync($"/api/orders/{paid}/confirm-payment", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertReconciledAsync(productId, onHand: 3, reserved: 0);            // بيع −2

        (await CancelAsync(admin, paid)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertReconciledAsync(productId, onHand: 5, reserved: 0);            // طلب مدفوع لم يُشحن: +2

        var pending = await PlaceAsync(customer, productId, 1);
        (await CancelAsync(admin, pending)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertReconciledAsync(productId, onHand: 5, reserved: 0);            // تحرير بلا سطر سجلّ

        await AdjustAsync(admin, productId, -1, "جرد");
        await AssertReconciledAsync(productId, onHand: 4, reserved: 0);

        (await LedgerOf(productId)).Should().Equal(
            (StockMovementType.Purchase, 5), (StockMovementType.Sale, -2),
            (StockMovementType.Cancellation, 2), (StockMovementType.Adjustment, -1));
    }

    [Fact]
    public async Task إلغاء_طلب_معلّق_يحرّر_حجزه_مرّة_واحدة()
    {
        // Phase 0 C2/C10: الإلغاء كان لا يعيد المحجوز، والإلغاء المكرّر كان يضاعف الإعادة.
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        var orderId = await PlaceAsync(customer, productId, 2);

        (await CancelAsync(admin, orderId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertReconciledAsync(productId, onHand: 5, reserved: 0);

        var again = await CancelAsync(admin, orderId);
        (await ProblemAsync(again)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidOrderOperation"));
        await AssertReconciledAsync(productId, onHand: 5, reserved: 0);
        (await LedgerOf(productId)).Should().Equal((StockMovementType.Purchase, 5));
    }

    [Fact]
    public async Task التصحيح_بفارق_لا_يمحو_بيعاً_ولا_ينزل_تحت_المحجوز_وتعديل_المنتج_لا_يمسّ_المخزون()
    {
        // Phase 0 C4: النموذج كان يرسل المخزون المطلق الذي رآه المدير فيمحو بيعاً حدث أثناء فتحه.
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        var sold = await PlaceAsync(customer, productId, 1);
        (await customer.PostAsync($"/api/orders/{sold}/confirm-payment", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var level = await AdjustAsync(admin, productId, 10, "توريد جديد");
        level.Should().Be(new StockLevelBody(productId, 14, 0, 14, 5, false));   // 4 بعد البيع + 10، لا 15 ولا 10

        await PlaceAsync(customer, productId, 3);                                  // (14، محجوز 3)
        var belowReserved = await admin.PostAsJsonAsync($"/api/admin/inventory/{productId}/adjustments", new { delta = -12, reason = "جرد" });
        (await ProblemAsync(belowReserved)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidInventoryOperation"));

        // تعديل المنتج لا يحمل مخزوناً: حقل مخزون مُرسَل يُتجاهَل فلا طريق لكتابة مطلقة.
        var current = await _api.WithDbAsync(db =>
            db.Products.Where(p => p.Id == productId).Select(p => new { p.CategoryId, p.Slug }).SingleAsync());
        (await admin.PutAsJsonAsync($"/api/products/{productId}", new
        {
            categoryId = current.CategoryId, slug = current.Slug, translations = new { ar = new { name = "اسم" } },
            price = 10m, stockQuantity = 999, expectedStockQuantity = 14,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertReconciledAsync(productId, onHand: 14, reserved: 3);
    }

    [Fact]
    public async Task انتهاء_المهلة_يلغي_الطلب_المهجور_ويحرّر_حجزه()
    {
        // Phase 0 C6: طلب هُجر كان يحجز المخزون للأبد. المنسّق يلغيه بعد المهلة — بعد أن تؤكّد البوّابة إلغاء نيّته.
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 4);
        var (customer, _) = await _api.NewCustomerAsync();
        var orderId = await PlaceAsync(customer, productId, 3);
        await AssertReconciledAsync(productId, onHand: 4, reserved: 3);

        await using (var scope = await _factory.TenantScopeAsync())
        {
            var reference = $"order:{orderId}";
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().StockReservations
                .Where(r => r.Reference == reference)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ExpireStaleCheckoutsCommand(Max: 500)))
                .Should().BeGreaterThanOrEqualTo(1);
        }

        (await _api.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()))
            .Should().Be(OrderStatus.Cancelled);
        (await _api.WithDbAsync(db => db.StockReservations.Where(r => r.Reference == $"order:{orderId}").Select(r => r.Status).SingleAsync()))
            .Should().Be(ReservationStatus.Expired);
        await AssertReconciledAsync(productId, onHand: 4, reserved: 0);
    }

    [Fact]
    public async Task الدينار_بثلاث_خانات_يُخزَّن_ويُحسَب_بلا_تقريب_صامت()
    {
        // Phase 0 C5: decimal(18,2) كان يقرّب 12.345 إلى 12.35، والخصم المئوي يختلف بين
        // الذاكرة (المُرسَل للبوّابة) والقاعدة (المعروض على الطلب).
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 12.345m, stock: 5);
        var code = $"IT15{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        (await admin.PostAsJsonAsync("/api/coupons", new { code, type = "Percentage", value = 15 }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var product = await _api.Anonymous().GetFromJsonAsync<PriceBody>($"/api/products/{productId}", TestApi.Json);
        product!.Price.Should().Be(12.345m);

        var (customer, _) = await _api.NewCustomerAsync();
        var response = await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان", items = new[] { new { productId, quantity = 1 } }, couponCode = code,
        });
        var order = await response.Content.ReadFromJsonAsync<OrderTotalsBody>(TestApi.Json);

        order!.Subtotal.Should().Be(12.345m);
        order.DiscountAmount.Should().Be(1.852m);   // 15% = 1.85175 ⇒ تقريب تجاري للفلس
        order.TotalAmount.Should().Be(10.493m);
        var storedDiscount = await _api.WithDbAsync(db =>
            db.Orders.Where(o => o.Id == order.OrderId).Select(o => o.DiscountAmount!.Amount).SingleAsync());
        storedDiscount.Should().Be(1.852m);
    }

    [Fact]
    public async Task سعر_بخانات_أكثر_من_العملة_يُرفض_بـ_422_لا_500()
    {
        var admin = await _api.AdminAsync();
        var categoryId = (await admin.GetFromJsonAsync<List<TestApi.IdBody>>("/api/categories", TestApi.Json))!.First().Id;

        var response = await admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(categoryId, 12.3456m, 1, "دقّة زائدة"));

        // قاعدة يحرسها Money (خانات العملة) ⇒ 422 برمز ثابت، لا 500 ولا نص استثناء خام.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("InvalidMoney");
    }

    // ── أدوات ──────────────────────────────────────────────────────────────────

    private async Task<int> PlaceAsync(HttpClient customer, int productId, int quantity)
    {
        var response = await _api.PlaceOrderAsync(customer, productId, quantity);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
    }

    private static Task<HttpResponseMessage> CancelAsync(HttpClient admin, int orderId) =>
        admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Cancel" });

    private static async Task<StockLevelBody> AdjustAsync(HttpClient admin, int productId, int delta, string reason)
    {
        var response = await admin.PostAsJsonAsync($"/api/admin/inventory/{productId}/adjustments", new { delta, reason });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StockLevelBody>(TestApi.Json))!;
    }

    // Σ حركات السجلّ = الموجود، وΣ الحجوزات النشطة = المحجوز — ثابتا المرحلة 6 في كل خطوة.
    private async Task AssertReconciledAsync(int productId, int onHand, int reserved)
    {
        var state = await _api.WithDbAsync(async db => new
        {
            Item = await db.InventoryItems.Where(i => i.ProductId == productId).Select(i => new { i.OnHand, i.Reserved }).SingleAsync(),
            Ledger = await db.StockMovements.Where(m => m.ProductId == productId).SumAsync(m => m.QuantityChange),
            Active = await db.StockReservations
                .Where(r => r.Status == ReservationStatus.Active
                            && db.InventoryItems.Any(i => i.Id == r.InventoryItemId && i.ProductId == productId))
                .SumAsync(r => r.Quantity),
        });

        (state.Item.OnHand, state.Item.Reserved).Should().Be((onHand, reserved));
        state.Ledger.Should().Be(state.Item.OnHand, "Σ حركات السجلّ = الموجود");
        state.Active.Should().Be(state.Item.Reserved, "Σ الحجوزات النشطة = المحجوز");
    }

    private Task<List<(StockMovementType Type, int QuantityChange)>> LedgerOf(int productId) => _api.WithDbAsync(async db =>
        (await db.StockMovements.Where(m => m.ProductId == productId).OrderBy(m => m.Id)
            .Select(m => new { m.Type, m.QuantityChange }).ToListAsync())
        .Select(m => (m.Type, m.QuantityChange)).ToList());

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record StockLevelBody(int ProductId, int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLowStock);
    private sealed record PriceBody(decimal Price);
    private sealed record OrderTotalsBody(int OrderId, decimal Subtotal, decimal DiscountAmount, decimal TotalAmount);
}
