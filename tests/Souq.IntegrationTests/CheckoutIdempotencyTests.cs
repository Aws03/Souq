using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Enums;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// F-8 — ما يفعله إرسال الدفع مرّتين، اليوم، بالقياس لا بالقراءة.
//
// هذه الاختبارات **توثّق سلوكاً قائماً غير مرغوب**، لا تباركه. قرار "ماذا يجب أن يحدث"
// تجاري (يُرفض؟ يُعاد الطلب نفسه؟) وهو معزول للمالك في ReleaseReadiness. ما يثبت هنا هو
// النطاق الحقيقي للمشكلة، كي يُتَّخذ القرار على وقائع:
//   • كم طلباً يُنشأ فعلاً،
//   • وكم يُحجز من المخزون،
//   • وهل تتضاعف الجباية (لا — وهذا هو الفارق بين "إزعاج" و"كارثة مالية").
// حين يُنفَّذ القرار، هذه الاختبارات هي التي يجب أن تُقلَب، فتصير وصفاً للسلوك الجديد.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CheckoutIdempotencyTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public CheckoutIdempotencyTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task إرسال_الدفع_مرّتين_ينشئ_طلبين_ويحجز_المخزون_مرّتين()
    {
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 10);
        var (customer, _) = await _api.NewCustomerAsync();

        var first = await _api.PlaceOrderAsync(customer, productId, 1);
        var second = await _api.PlaceOrderAsync(customer, productId, 1);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created, "لا مفتاح تكرار ولا شرط على طلب معلّق قائم");

        var firstId = (await first.Content.ReadFromJsonAsync<OrderCreated>(TestApi.Json))!.OrderId;
        var secondId = (await second.Content.ReadFromJsonAsync<OrderCreated>(TestApi.Json))!.OrderId;
        secondId.Should().NotBe(firstId, "طلبان مستقلّان من ضغطة مكرّرة واحدة");

        // والحجز يتضاعف: قطعتان محجوزتان من عميل أراد واحدة.
        var reserved = await _api.WithDbAsync(db => db.InventoryItems
            .Where(i => i.ProductId == productId).SumAsync(i => i.Reserved));
        reserved.Should().Be(2);
    }

    [Fact]
    public async Task ضغطتان_متزامنتان_تماماً_تنشئان_طلبين_أيضاً()
    {
        // النقر المزدوج الحقيقي: لا تسلسل بين الطلبين. لا شيء يسلسلهما اليوم.
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 10);
        var (customer, _) = await _api.NewCustomerAsync();

        var responses = await Task.WhenAll(
            _api.PlaceOrderAsync(customer, productId, 1),
            _api.PlaceOrderAsync(customer, productId, 1));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(2);
    }

    [Fact]
    public async Task لكن_لا_جباية_مزدوجة_ولا_طلب_يائس_إلى_الأبد()
    {
        // الحدّ الذي يجعلها P1 لا P0: نيّتا دفع منفصلتان، والواجهة تعرض واحدة، فلا تُجبى
        // بطاقة مرّتين من هذا المسار. والطلب الثاني المعلّق يُصفّى بانتهاء مهلة الحجز
        // (ExpireStaleCheckouts) فيعود مخزونه وكوبونه — التلف مؤقّت لا دائم.
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 10);
        var (customer, _) = await _api.NewCustomerAsync();

        var first = await (await _api.PlaceOrderAsync(customer, productId, 1)).Content
            .ReadFromJsonAsync<OrderCreated>(TestApi.Json);
        var second = await (await _api.PlaceOrderAsync(customer, productId, 1)).Content
            .ReadFromJsonAsync<OrderCreated>(TestApi.Json);

        first!.ClientSecret.Should().NotBeNullOrEmpty();
        second!.ClientSecret.Should().NotBeNullOrEmpty();
        second.ClientSecret.Should().NotBe(first.ClientSecret, "نيّتا دفع مستقلّتان — لا جباية واحدة مكرّرة");

        var statuses = await _api.WithDbAsync(db => db.Orders
            .Where(o => o.Id == first.OrderId || o.Id == second.OrderId)
            .Select(o => o.Status).ToListAsync());
        statuses.Should().AllBeEquivalentTo(OrderStatus.Pending, "كلاهما معلّق، ومصير المعلّق الانتهاء لا البقاء");
    }

    private sealed record OrderCreated(int OrderId, string ClientSecret);
}
