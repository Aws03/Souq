using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// حصص الخطط (C2، ADR-0049) على قاعدة حقيقية.
//
// **لماذا هنا لا في اختبارات الوحدة:** ما يُختبر هو سلوك القاعدة تحت التزامن — قفلُ صفٍّ وإعادةُ
// تقييم شرطٍ بعد انتظاره. ضعفٌ في الذاكرة يثبت أنّ الشيفرة تُنادي ما يُفترض، ولا يثبت شيئاً عمّا
// يفعله محرّك القاعدة، وهو المكان الوحيد الذي يقع فيه الخلل.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class TenantQuotaTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public TenantQuotaTests(SouqApiFactory factory)
    {
        _factory = factory; _api = new TestApi(factory);
    }

    [Fact]
    public async Task خطة_بلا_حدّ_لا_تقيّد_شيئاً()
    {
        // حال كل متجر اليوم: الخطة التأسيسية بلا حدود. الغياب = غير مقيَّد لا صفر (ADR-0054)،
        // ولو كان صفراً لما استطاع أيّ متجر قائم إنشاء منتج واحد بعد هذه المرحلة.
        var store = await _factory.CreateStoreAsync();
        var admin = await _api.ForStore(store).AdminAsync();
        var category = await _api.ForStore(store).CreateCategoryAsync(admin);

        for (var i = 0; i < 3; i++)
            await _api.ForStore(store).CreateProductAsync(admin, categoryId: category, name: $"منتج {i}");

        (await ProductCountAsync(store)).Should().Be(3);
    }

    [Fact]
    public async Task الحدّ_يرفض_ما_بعد_السقف_برمز_ثابت()
    {
        var store = await _factory.CreateStoreOnPlanWithLimitsAsync((LimitNames.CatalogProducts, 2));
        var storeApi = _api.ForStore(store);
        var admin = await storeApi.AdminAsync();
        var category = await storeApi.CreateCategoryAsync(admin);

        await storeApi.CreateProductAsync(admin, categoryId: category, name: "الأوّل");
        await storeApi.CreateProductAsync(admin, categoryId: category, name: "الثاني");

        var refused = await admin.PostAsJsonAsync("/api/products",
            TestApi.ProductBody(category, 10m, 1, "الثالث"), TestApi.Json);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await refused.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json);
        problem!.Code.Should().Be("QuotaExceeded", "العميل يتفرّع على الرمز لا على الرسالة");

        (await ProductCountAsync(store)).Should().Be(2, "الرفض امتناعٌ عن الكتابة لا تراجعٌ عنها");
    }

    // ============================================================================
    // **الاختبار الذي يستطيع أن يفشل** — ADR-0049 §الالتزام الرابع، وهو مكتوب هناك ردّاً على
    // `LastAdministratorConcurrencyTests` التي "تمرّ بالحساب ولو حُذف الحارس كلّه".
    //
    // الترتيب مقصود بالكامل: الحدّ ثلاثة، ومنتجان موجودان، فالمقعد المتبقّي **واحد** ويتسابق عليه
    // طلبان. الحارس السليم يعطيه لأحدهما ويرفض الآخر؛ والحارس المكسور — أو أيّ عودة إلى "عُدّ ثم
    // اكتب" — يُمرّرهما معاً فيصير العدد أربعة. أي أنّ الحالة الممنوعة **قابلة للبلوغ فعلاً**، وهو
    // بالضبط ما لا تستطيعه تلك الاختبارات الأخرى.
    //
    // ويُعاد خمس مرّات: السباق زمنيّ، ومرورٌ أخضر واحد لا يثبت شيئاً.
    // ============================================================================
    [Fact]
    public async Task طلبان_متزامنان_على_آخر_مقعد_لا_يمرّان_معاً()
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var store = await _factory.CreateStoreOnPlanWithLimitsAsync((LimitNames.CatalogProducts, 3));
            var storeApi = _api.ForStore(store);
            var admin = await storeApi.AdminAsync();
            var category = await storeApi.CreateCategoryAsync(admin);

            await storeApi.CreateProductAsync(admin, categoryId: category, name: "قائم ١");
            await storeApi.CreateProductAsync(admin, categoryId: category, name: "قائم ٢");

            var responses = await Task.WhenAll(
                admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(category, 10m, 1, "متسابق أ"), TestApi.Json),
                admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(category, 10m, 1, "متسابق ب"), TestApi.Json));

            var statuses = responses.Select(r => r.StatusCode).ToList();

            (await ProductCountAsync(store)).Should().Be(3,
                $"المحاولة {attempt}: الردّان {statuses[0]} و {statuses[1]} — الحدّ ثلاثة ولا يُتجاوَز تحت أي تزامن");

            statuses.Count(s => s == HttpStatusCode.Created).Should().Be(1, $"المحاولة {attempt}: مقعد واحد");
            statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(1, $"المحاولة {attempt}");
        }
    }

    // ============================================================================
    // الأرشفة تُفرغ مقعداً والاستعادة تشغله. بلا الثانية يُتجاوَز الحدّ بأرشفةٍ واستعادة — ثغرةٌ
    // لا يكشفها اختبار إنشاء واحد، ولا مسار حذفٍ فعليّ للمنتج أصلاً يجعلها نظرية.
    // ============================================================================
    [Fact]
    public async Task الأرشفة_تُفرغ_مقعداً_والاستعادة_تشغله()
    {
        var store = await _factory.CreateStoreOnPlanWithLimitsAsync((LimitNames.CatalogProducts, 1));
        var storeApi = _api.ForStore(store);
        var admin = await storeApi.AdminAsync();
        var category = await storeApi.CreateCategoryAsync(admin);

        var first = await storeApi.CreateProductAsync(admin, categoryId: category, name: "الوحيد");

        // ممتلئ.
        (await admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(category, 10m, 1, "زائد"), TestApi.Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // أرشفة ⇒ المقعد حرّ.
        (await admin.DeleteAsync($"/api/products/{first}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var second = await storeApi.CreateProductAsync(admin, categoryId: category, name: "البديل");

        // واستعادة المؤرشف الآن تُرفض: المقعد شغله البديل.
        var restore = await admin.PutAsJsonAsync($"/api/admin/products/{first}/status",
            new { status = nameof(ProductStatus.Active) }, TestApi.Json);
        restore.StatusCode.Should().Be(HttpStatusCode.Conflict, "الاستعادة تحجز كالإنشاء");

        (await ProductCountAsync(store)).Should().Be(1);
        second.Should().BePositive();
    }

    // ============================================================================
    // العدّاد يُنشأ من **العدّ الفعلي** لا من صفر. متجرٌ ملأ كتالوجه قبل أن توجد الحصص أصلاً — وهو
    // حال كل متجر قائم لحظة هذه الهجرة — لا يجوز أن تُهدى له حصّةٌ كاملة عند أوّل حجز.
    // ============================================================================
    [Fact]
    public async Task عدّاد_متجر_قائم_يبدأ_من_عدده_الحقيقي_لا_من_صفر()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var admin = await storeApi.AdminAsync();
        var category = await storeApi.CreateCategoryAsync(admin);

        for (var i = 0; i < 3; i++)
            await storeApi.CreateProductAsync(admin, categoryId: category, name: $"سابق {i}");

        // يُحذف صفّ العدّاد: هذه بالضبط حال كل متجر لحظة هذه الهجرة — كتالوجٌ قائم ولا عدّاد له.
        // (والحارس يُبقي العدّاد محدَّثاً حتى بلا حدّ، فبغير هذا الحذف لا يُختبَر مسار البذر إطلاقاً.)
        await using (var scope = await _factory.TenantScopeAsync(store.Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantUsageCounters.RemoveRange(await db.TenantUsageCounters.ToListAsync());
            await db.SaveChangesAsync();
        }

        await PutOnPlanWithLimitAsync(store, LimitNames.CatalogProducts, 3);

        // السقف ثلاثة والموجود ثلاثة: أوّل محاولة تُرفض — لا تُقبل ثلاثاً أخرى.
        (await admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(category, 10m, 1, "رابع"), TestApi.Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await CounterAsync(store, LimitNames.CatalogProducts)).Should().Be(3);
    }

    // ============================================================================
    // المصالحة تُصلح الانحراف. يُصطنع هنا بتعديل العدّاد مباشرةً في القاعدة — وهو أحد مصادر
    // الانحراف الحقيقية التي يوجد المسح من أجلها (يدٌ في القاعدة، مسارٌ نسي الإطلاق، محوُ بيانات).
    // ============================================================================
    [Fact]
    public async Task المصالحة_تُعيد_العدّاد_إلى_الحقيقة()
    {
        var store = await _factory.CreateStoreOnPlanWithLimitsAsync((LimitNames.CatalogProducts, 10));
        var storeApi = _api.ForStore(store);
        var admin = await storeApi.AdminAsync();
        var category = await storeApi.CreateCategoryAsync(admin);

        await storeApi.CreateProductAsync(admin, categoryId: category, name: "منتج");
        (await CounterAsync(store, LimitNames.CatalogProducts)).Should().Be(1);

        await using (var scope = await _factory.TenantScopeAsync(store.Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var counter = await db.TenantUsageCounters.FirstAsync(c => c.Name == LimitNames.CatalogProducts);
            db.Entry(counter).Property(nameof(TenantUsageCounter.Used)).CurrentValue = 9;
            await db.SaveChangesAsync();
        }

        await using (var scope = await _factory.TenantScopeAsync(store.Tenant))
        {
            var corrected = await scope.ServiceProvider
                .GetRequiredService<Souq.Application.Features.Billing.Contracts.ITenantQuotaGuard>()
                .ReconcileAsync();
            corrected.Should().Be(1, "عدّاد واحد كان منحرفاً");
        }

        (await CounterAsync(store, LimitNames.CatalogProducts)).Should().Be(1);
    }

    // ============================================================================
    // مقاعد الموظّفين: الدعوة تحجز، والإيقاف يُفرغ. ولا مسار حذفٍ لحساب موظّف — فالإيقاف هو
    // الطريقة الوحيدة لتفريغ مقعد، ولو لم يُفرغه لصار الحدّ سقّاطة (LimitNames).
    // ============================================================================
    [Fact]
    public async Task مقعد_الموظّف_يُحجَز_بالدعوة_ويُفرَّغ_بالإيقاف()
    {
        // المدير المنشأ مع المتجر يشغل مقعداً، فالسقف اثنان يترك مقعداً واحداً.
        var store = await _factory.CreateStoreOnPlanWithLimitsAsync((LimitNames.StaffSeats, 2));
        var storeApi = _api.ForStore(store);
        var admin = await storeApi.AdminAsync();

        var invited = await admin.PostAsJsonAsync("/api/admin/staff",
            new { fullName = "موظّف أوّل", email = $"s1-{Guid.NewGuid():N}@souq.test", role = "TenantStaff" }, TestApi.Json);
        invited.StatusCode.Should().Be(HttpStatusCode.OK);

        var overflow = await admin.PostAsJsonAsync("/api/admin/staff",
            new { fullName = "موظّف ثانٍ", email = $"s2-{Guid.NewGuid():N}@souq.test", role = "TenantStaff" }, TestApi.Json);
        overflow.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await overflow.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("QuotaExceeded");

        // إيقاف الأوّل يُفرغ مقعده، فالثاني يمرّ.
        var staff = (await admin.GetFromJsonAsync<TestApi.PageBody<StaffRow>>("/api/admin/staff?pageSize=100", TestApi.Json))!;
        var firstId = staff.Items.Single(s => s.Email.StartsWith("s1-", StringComparison.Ordinal)).Id;
        (await admin.PostAsJsonAsync($"/api/admin/staff/{firstId}/status", new { active = false }, TestApi.Json))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await admin.PostAsJsonAsync("/api/admin/staff",
                new { fullName = "موظّف ثالث", email = $"s3-{Guid.NewGuid():N}@souq.test", role = "TenantStaff" }, TestApi.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task PutOnPlanWithLimitAsync(TestStore store, string limitName, int value)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = new Plan($"lim-{Guid.NewGuid():N}"[..24], 1, "خطة محدودة");
        plan.SetEntitlements(StoreModules.All);
        plan.SetLimits([new Limit(limitName, value)]);
        plan.Publish();
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        var subscription = await db.Subscriptions.FirstAsync(s => s.TenantId == store.Tenant.Id);
        subscription.ChangePlan(plan, DateTime.UtcNow);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<Souq.Application.Common.Tenancy.ITenantDirectory>().InvalidateAsync();
    }

    private async Task<int> ProductCountAsync(TestStore store)
    {
        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Products.AsNoTracking().CountAsync(p => p.Status != ProductStatus.Archived);
    }

    private async Task<int?> CounterAsync(TestStore store, string limitName)
    {
        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .TenantUsageCounters.AsNoTracking()
            .Where(c => c.Name == limitName).Select(c => (int?)c.Used).FirstOrDefaultAsync();
    }

    private sealed record StaffRow(int Id, string Email);
}
