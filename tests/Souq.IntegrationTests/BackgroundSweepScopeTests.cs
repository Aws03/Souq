using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// أي متاجر تمرّ عليها المهام الخلفية (R-24، قُرِّر في M5).
//
// المهام الدورية — انتهاء مهلة الدفع وتنظيف السلال — كانت تمرّ على المتاجر **النشطة وحدها**. ومتجر موقوف لا
// يستطيع متسوّقوه إتمام أي دفع (البوّابة مغلقة عليهم)، فحجوزات مخزونه المنتهية كانت لا يُصفّيها شيء أبداً:
// مخزون محجوز لطلبات لن تكتمل، وسلال تتراكم — ويظهر الخطأ عند إعادة تفعيل المتجر لا عند إيقافه، وهو أسوأ وقت
// لاكتشافه.
//
// هذا الاختبار هو القرار مكتوباً: **النشط والموقوف داخل المسح، والمُهيَّأ والمؤرشف خارجه.** لو وُسِّع المسح
// لاحقاً أو ضُيِّق بلا قصد، يسقط هنا بدل أن يظهر في متجر زبون بعد أشهر.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class BackgroundSweepScopeTests
{
    private readonly SouqApiFactory _factory;

    public BackgroundSweepScopeTests(SouqApiFactory factory) => _factory = factory;

    [Fact]
    public async Task المسح_يشمل_الموقوف_لا_النشط_وحده()
    {
        var active = await _factory.CreateStoreAsync(TenantStatus.Active);
        var suspended = await _factory.CreateStoreAsync(TenantStatus.Suspended);

        var swept = await SweptStoreIdsAsync();

        swept.Should().Contain(active.Tenant.Id);
        swept.Should().Contain(suspended.Tenant.Id,
            "متجر موقوف لا يُتمّ متسوّقوه دفعاً، فحجوزاته المنتهية لا يُصفّيها شيء آخر — وهذا هو R-24 بعينه");
    }

    [Fact]
    public async Task المُهيَّأ_والمؤرشف_خارج_المسح()
    {
        var provisioning = await _factory.CreateStoreAsync(TenantStatus.Provisioning);
        var archived = await _factory.CreateStoreAsync(TenantStatus.Archived);

        var swept = await SweptStoreIdsAsync();

        swept.Should().NotContain(provisioning.Tenant.Id,
            "متجر قيد التهيئة لم يخدم متسوّقاً بعد، فلا حجز لديه ينتهي ولا سلّة تُنظَّف");
        swept.Should().NotContain(archived.Tenant.Id,
            "المؤرشف نهائي: تعديل بياناته عملٌ على سجلّ مغلق لا صيانة");
    }

    private async Task<List<int>> SweptStoreIdsAsync()
    {
        // الدليل يُقرأ في نطاق متجر كما تفعل المهمة الخلفية نفسها؛ جدول المتاجر جدول منصّة يراه المرشّح.
        await using var scope = await _factory.TenantScopeAsync();
        var directory = scope.ServiceProvider.GetRequiredService<ITenantDirectory>();
        var stores = await directory.ListForBackgroundSweepsAsync();
        return stores.Select(s => s.Id).ToList();
    }
}
