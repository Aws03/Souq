using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// F-7 — متجر بلا مدير.
//
// القاعدة المقصودة مكتوبة في الشيفرة منذ البداية: لا يُوقَف آخر حساب فعّال بالدور الأعلى.
// لكن الفحص والكتابة كانا منفصلين: طلبان متزامنان يوقفان مديرَين *مختلفين* يعدّان كلاهما 2
// قبل أن يكتب أيٌّ منهما، فيمرّان معاً — وrowversion لا يتصادم لأن كلاً منهما يكتب صفّاً آخر.
//
// النتيجة ليست إزعاجاً: المتجر يصبح غير قابل للإدارة من داخله. يبقى مسار المنصّة لاستعادته،
// وهذا ما يجعلها P1 لا P0 — لكنها تحتاج زبوناً واحداً ليكتشفها في أسوأ لحظة.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class LastAdministratorConcurrencyTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public LastAdministratorConcurrencyTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task إيقاف_مديرَين_في_اللحظة_نفسها_لا_يترك_المتجر_بلا_مدير()
    {
        // خمس محاولات: السباق توقيتي، ومحاولة واحدة ناجحة لا تُثبت شيئاً.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var store = await _factory.CreateStoreAsync();
            var storeApi = _api.ForStore(store);

            // ثلاثة مديرين: مدير المتجر الأصلي + اثنان يوقفهما طلبان متزامنان. (المدير الأصلي
            // هو من يُصدر الطلبين، ولا يستطيع إيقاف نفسه — فالمتسابقان هما الآخران.)
            var firstEmail = await _factory.CreateStoreUserAsync(store.Tenant, Roles.TenantAdmin);
            var secondEmail = await _factory.CreateStoreUserAsync(store.Tenant, Roles.TenantAdmin);
            var admin = await storeApi.AdminAsync();

            var staff = (await admin.GetFromJsonAsync<TestApi.PageBody<AccountBody>>(
                "/api/admin/staff?pageSize=100", TestApi.Json))!;
            var first = staff.Items.Single(a => a.Email == firstEmail).Id;
            var second = staff.Items.Single(a => a.Email == secondEmail).Id;

            var body = new { active = false };
            var responses = await Task.WhenAll(
                admin.PostAsJsonAsync($"/api/admin/staff/{first}/status", body),
                admin.PostAsJsonAsync($"/api/admin/staff/{second}/status", body));

            var remaining = await ActiveAdminsAsync(store);
            remaining.Should().BeGreaterThan(0,
                $"المحاولة {attempt}: الردّان {responses[0].StatusCode} و {responses[1].StatusCode} — " +
                "لا يجوز أن يُترك المتجر بلا مدير مهما تزامنت الطلبات");

            // ومن يخسر السباق يحصل على رفض عمل واضح، لا خطأ خادم ولا جمود قاعدة بيانات.
            foreach (var response in responses)
                response.StatusCode.Should().BeOneOf(
                    HttpStatusCode.NoContent, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task آخر_مدير_وحيد_لا_يُوقَف_إطلاقاً()
    {
        // المسار السريع، غير المتسابق: يبقى رفضاً فورياً واضحاً.
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var otherEmail = await _factory.CreateStoreUserAsync(store.Tenant, Roles.TenantAdmin);
        var admin = await storeApi.AdminAsync();

        var staff = (await admin.GetFromJsonAsync<TestApi.PageBody<AccountBody>>(
            "/api/admin/staff?pageSize=100", TestApi.Json))!;
        var other = staff.Items.Single(a => a.Email == otherEmail).Id;

        // إيقاف الأول ينجح (يبقى مديران: الأصلي والمُصدِر نفسه).
        (await admin.PostAsJsonAsync($"/api/admin/staff/{other}/status", new { active = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ولا يستطيع المُصدِر إيقاف نفسه، فيبقى المتجر بمدير دائماً.
        (await ActiveAdminsAsync(store)).Should().BeGreaterThan(0);
    }

    private async Task<int> ActiveAdminsAsync(TestStore store)
    {
        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<Souq.Infrastructure.Persistence.AppDbContext>();
        return await db.Users.CountAsync(u =>
            u.Role == Roles.TenantAdmin && u.Status == UserStatus.Active && u.PasswordHash != "");
    }

    private sealed record AccountBody(int Id, string Email);
}
