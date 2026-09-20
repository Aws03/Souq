using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Features.Baskets;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// F-28 — تعارض تزامن عند دمج سلّة الزائر.
//
// أول قراءةٍ للسلّة بعد الدخول ليست قراءة: `BasketResolver` يضمّ سلّة الزائر إلى سلّة العميل
// ثم **يحذفها**. والواجهة تُطلق `/api/basket` و`/api/basket/quote` معاً عند إقلاع الصفحة، فتدخل
// الطلبتان هذا المسار على السلّة نفسها: تفوز إحداهما بالحذف، وتجد الأخرى `DELETE` بلا صفوف ⇒
// `DbUpdateConcurrencyException` ⇒ **409** على طلب `GET`.
//
// قِيس على حزمة تعمل قبل الإصلاح: قراءتان متزامنتان لعميلٍ يحمل رمز سلّة زائر ⇒ **200 و409**،
// وضابطٌ بلا سلّة زائر ⇒ **200 و200**. أي أنّ العطل في الدمج تحديداً لا في التزامن عموماً —
// وكل مشترٍ ملأ سلّته ثم سجّل دخوله كان معرَّضاً له.
//
// الحارس لا يُمسّ: التعارض يمنع دمجاً مزدوجاً يُضاعف أسطر السلّة، وهو صحيح. ما تغيّر أن
// الخاسرة تُعيد المحاولة على الحالة الملتزمة (`BasketWriter`) فتجد سلّة الزائر محذوفة ولا
// تدمج، وتُعيد السلّة المدموجة — الجواب الذي كان الـ409 يحجبه.
//
// الاختبارات تُطلق الطلبات متزامنةً فعلاً، وتفحص **كل** ردّ: لا 409 مخبَّأ خلف "أحدها نجح".
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class BasketConcurrencyTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public BasketConcurrencyTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    // ستّة: الواجهة تُطلق طلبين، والعدد الأكبر يجعل السباق مؤكّداً لا محظوظاً.
    private const int Concurrent = 6;
    private const string CustomerPassword = "Customer-Pass-1";

    [Fact]
    public async Task قراءات_متزامنة_للحظة_الدمج_كلها_تنجح_ولا_تُضاعف_الأسطر()
    {
        // ثلاث جولات: السباق توقيتي، وجولةٌ نظيفة واحدة لا تُثبت شيئاً.
        for (var round = 1; round <= 3; round++)
        {
            var admin = await _api.AdminAsync();
            var productId = await _api.CreateProductAsync(admin, price: 4m, stock: 50);
            var (_, email) = await _api.NewCustomerAsync();

            // المتصفّح نفسه: زائراً يضيف صنفاً، ثم يدخل فيحمل رمز الزائر والجلسة معاً —
            // وهذه هي اللحظة التي يقع فيها الدمج.
            var browser = _api.SecureClient();
            var seeded = await browser.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 3 });
            seeded.EnsureSuccessStatusCode();
            browser.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await _api.TokenAsync(email, CustomerPassword));

            // تُطلق كلها قبل انتظار أيّ منها — وإلّا صارت متتابعة والاختبار بلا معنى.
            var responses = await Task.WhenAll(Enumerable.Range(0, Concurrent)
                .Select(_ => browser.GetAsync("/api/basket")).ToArray());

            var statuses = string.Join(", ", responses.Select(r => (int)r.StatusCode));
            responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.Conflict,
                $"الجولة {round}: دمج سلّة الزائر لا يجوز أن يُنتج تعارضاً على قراءة (F-28) — الردود: {statuses}");
            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
                $"الجولة {round}: كل قراءة للسلّة تنجح مهما تزامنت — الردود: {statuses}");

            // والدمج وقع **مرّة واحدة**: ستّ قراءات متزامنة لا تضيف الصنف ستّ مرّات.
            var baskets = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<TestApi.BasketBody>(TestApi.Json)));
            foreach (var basket in baskets)
            {
                basket.Should().NotBeNull();
                basket!.Lines.Should().ContainSingle($"الجولة {round}: سطر واحد للمنتج الواحد مهما تكرّرت القراءة")
                    .Which.Quantity.Should().Be(3, $"الجولة {round}: الكمية هي ما وضعه الزائر، لا مضاعفاتها");
            }

            // وسلّة الزائر اختفت فعلاً من القاعدة — الدمج تمّ ولم يُترك نصفه.
            var hash = GuestBasketTokens.Hash(TestApi.GuestBasketToken(seeded));
            (await _api.WithDbAsync(db => db.Baskets.CountAsync(b => b.GuestTokenHash == hash)))
                .Should().Be(0, $"الجولة {round}: سلّة الزائر تُحذف بعد دمجها");
        }
    }

    [Fact]
    public async Task عميل_بلا_سلّة_زائر_لا_يتنازع_أصلاً()
    {
        // الضابط الذي أثبت أنّ العطل في الدمج لا في التزامن: بلا رمز زائر لا يوجد حذف يُتنازع عليه.
        var (customer, _) = await _api.NewCustomerAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, Concurrent)
            .Select(_ => customer.GetAsync("/api/basket")).ToArray());

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            $"بلا دمج لا تعارض — الردود: {string.Join(", ", responses.Select(r => (int)r.StatusCode))}");
    }

    [Fact]
    public async Task إضافة_وقراءة_متزامنتان_عند_الدمج_لا_تُنتجان_تعارضاً_ولا_خطأ_خادم()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 4m, stock: 50);
        var other = await _api.CreateProductAsync(admin, price: 6m, stock: 50);
        var (_, email) = await _api.NewCustomerAsync();

        var browser = _api.SecureClient();
        (await browser.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })).EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _api.TokenAsync(email, CustomerPassword));

        // كتابةٌ وقراءاتٌ تدخل مسار الدمج نفسه في اللحظة نفسها — وهو ما تفعله صفحة تُقلع
        // بينما المتسوّق ينقر "أضف".
        var writes = Enumerable.Range(0, 2)
            .Select(_ => browser.PostAsJsonAsync("/api/basket/items", new { productId = other, quantity = 1 }));
        var reads = Enumerable.Range(0, Concurrent - 2).Select(_ => browser.GetAsync("/api/basket"));
        var responses = await Task.WhenAll(writes.Concat(reads).ToArray());

        var statuses = string.Join(", ", responses.Select(r => (int)r.StatusCode));
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.Conflict, $"لا تعارض — الردود: {statuses}");
        responses.Should().NotContain(r => (int)r.StatusCode >= 500, $"ولا خطأ خادم — الردود: {statuses}");
    }
}
