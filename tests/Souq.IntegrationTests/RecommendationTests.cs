using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Enums;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// المنتجاتُ ذاتُ الصلة (C10، ADR-0064) على قاعدةٍ حقيقية.
//
// ما يُحرس هنا هو **ترتيبُ قوّة الإشارة والصدقُ عند غيابها**:
//   • أنّ «اشتُريا معاً» يسبق «الفئة نفسها» حين توجد طلباتٌ سُلّمت،
//   • أنّ **طلباً واحداً لا يصنع توصية** — حدُّ التكرار هو ما يفصل الإشارة عن الصدفة،
//   • وأنّ متجراً بلا طلبات يُرجع الفئةَ والأحدث **بسببٍ مسمّى** بدل أن يدّعي توصية.
//
// والعزلُ بنيويّ: `Orders` يمرّ بمرشّح المستأجر، فالتجميع لا يرى طلبات متجرٍ آخر — وهو جواب
// `C-09` مفروضاً بالبناء. يحرسه `TenantIsolationTests` على المسار نفسه.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class RecommendationTests
{
    private readonly TestApi _api;

    public RecommendationTests(SouqApiFactory factory) => _api = new TestApi(factory);

    private sealed record Related(int Id, string? Reason);

    private async Task<IReadOnlyList<Related>> RelatedAsync(int productId) =>
        (await _api.Anonymous().GetFromJsonAsync<List<Related>>(
            $"/api/products/{productId}/related?count=6", TestApi.Json))!;

    // طلبٌ مُسلَّم بمنتجين: الحالةُ تُضبط في القاعدة لأنّ ما يُفحص هنا هو الاستعلام، ودورةُ
    // حياة الطلب محروسةٌ في `OrderLifecycleTests`.
    private async Task DeliveredOrderAsync(HttpClient customer, params (int ProductId, int Quantity)[] lines)
    {
        var response = await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }).ToArray(),
        });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json);
        var orderId = int.Parse(created!["orderId"].ToString()!);

        await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null);
        await _api.WithDbAsync(async db =>
        {
            await db.Orders.Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Delivered));
            return 0;
        });
    }

    [Fact]
    public async Task بلا_طلبات_تُرجَع_الفئة_ثم_الأحدث_بسبب_مسمّى_لا_بادّعاء_توصية()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var subject = await _api.CreateProductAsync(admin, categoryId: category);
        var sibling = await _api.CreateProductAsync(admin, categoryId: category);

        var related = await RelatedAsync(subject);

        related.Should().Contain(r => r.Id == sibling && r.Reason == "SameCategory");
        related.Should().NotContain(r => r.Reason == "BoughtTogether", "لا طلبات، فلا إشارة شراء");
        related.Should().OnlyContain(r => r.Reason != null, "كلُّ اقتراحٍ يحمل سببه");
    }

    // **حدُّ التكرار**: طلبٌ واحد جمع منتجين صدفةٌ لا إشارة، وعرضُها كتوصية يدّعي حساباً لم يقع.
    [Fact]
    public async Task طلب_واحد_لا_يصنع_توصية_شراء_معاً()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var subject = await _api.CreateProductAsync(admin, categoryId: category, stock: 50);
        var partner = await _api.CreateProductAsync(admin, categoryId: await _api.CreateCategoryAsync(admin), stock: 50);
        var (customer, _) = await _api.NewCustomerAsync();

        await DeliveredOrderAsync(customer, (subject, 1), (partner, 1));

        (await RelatedAsync(subject)).Should().NotContain(r => r.Id == partner && r.Reason == "BoughtTogether");
    }

    [Fact]
    public async Task طلبان_مُسلَّمان_يجعلان_الشراء_معاً_يسبق_الفئة()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var subject = await _api.CreateProductAsync(admin, categoryId: category, stock: 50);
        await _api.CreateProductAsync(admin, categoryId: category, stock: 50);   // جارٌ في الفئة
        var partner = await _api.CreateProductAsync(admin, categoryId: await _api.CreateCategoryAsync(admin), stock: 50);
        var (customer, _) = await _api.NewCustomerAsync();

        await DeliveredOrderAsync(customer, (subject, 1), (partner, 1));
        await DeliveredOrderAsync(customer, (subject, 1), (partner, 1));

        var related = await RelatedAsync(subject);

        related[0].Should().BeEquivalentTo(new Related(partner, "BoughtTogether"),
            "أقوى إشارةٍ يملكها متجرٌ بلا تتبّع تأتي أوّلاً");
        related.Should().NotContain(r => r.Id == subject, "المنتج لا يُقترح على نفسه");
    }
}
