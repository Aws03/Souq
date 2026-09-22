using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Exceptions;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// F-33 — الجمود على **قراءة** سباقٌ يُعاد، لا خطأ خادم.
//
// كان `AppDbContext` يترجم الجمود (SQL 1205) في `SaveChangesAsync` و`InTransactionAsync`، وكلاهما
// مسارُ كتابة. وSQL Server يختار ضحيّته بتكلفة التراجع لا بنوع الأمر، فيقع الاختيار على `SELECT`
// كما يقع على `UPDATE` — وحينها كان الاستثناء يخرج من تعداد النتائج خاماً فيصير **500**.
//
// وقع فعلاً: `GET /api/basket` أجاب 500 على مشغّل GitHub وفي التشغيل المحلّي، والسطر الملتقَط
// يقول «An exception occurred while iterating over the results of a query» ثمّ «Error Number:1205».
// و`BasketWriter` — المبنيّ لإعادة المحاولة على هذا السباق بعينه — لم يكن يرى ما يلتقطه.
//
// **وهذا الاختبار حتميّ، لا يستدعي حظّاً.** الجمود يُصنع بدورةٍ كلاسيكية: كلٌّ يقفل صفّاً ثم يطلب
// صفّ الآخر. و`SET DEADLOCK_PRIORITY LOW` يعيّن الضحيّة سلفاً، فيكون الأمرُ الفاشل **قراءةً**
// بعينها — وهي الفجوة التي كانت.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class DeadlockTranslationTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public DeadlockTranslationTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task جمود_تقع_ضحيّته_على_قراءة_يصل_تعارضاً_قابلاً_للإعادة()
    {
        var admin = await _api.AdminAsync();
        var mine = await _api.CreateProductAsync(admin, price: 4m, stock: 5);
        var yours = await _api.CreateProductAsync(admin, price: 5m, stock: 5);

        await using var winnerScope = await _factory.TenantScopeAsync();
        await using var victimScope = await _factory.TenantScopeAsync();
        var winner = winnerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var victim = victimScope.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var winnerTx = await winner.Database.BeginTransactionAsync();
        await using var victimTx = await victim.Database.BeginTransactionAsync();

        // الضحيّة تُعيَّن سلفاً كي يكون الأمرُ الفاشل قراءتَها هي — لا قرعة بين الطرفين.
        await victim.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY LOW");

        // كلٌّ يأخذ قفلاً حصرياً على صفّه: لمسةٌ لا تغيّر معنى الصف.
        await LockAsync(winner, mine);
        await LockAsync(victim, yours);

        // ثمّ يطلب كلٌّ صفَّ الآخر قراءةً ⇒ دورةُ انتظار ⇒ جمود.
        var winnerReads = ReadAsync(winner, yours);
        var victimReads = ReadAsync(victim, mine);

        var thrown = await Record.ExceptionAsync(() => Task.WhenAll(winnerReads, victimReads));

        thrown.Should().BeOfType<ConcurrencyConflictException>(
            "الجمود على قراءة يعني «أعد المحاولة» كما يعني على كتابة — ولا يجوز أن يصل المتسوّق 500");
        thrown!.InnerException.Should().BeOfType<SqlException>()
            .Which.Number.Should().Be(1205, "الأصلُ يُحفظ فلا يضيع رقم الخطأ من التشخيص");
    }

    // `UPDATE` بلا تغييرٍ في المعنى: يأخذ القفل الحصري وهذا كل المطلوب.
    private static Task LockAsync(AppDbContext db, int productId) =>
        db.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET UpdatedAt = SYSUTCDATETIME() WHERE Id = {0}", productId);

    // قراءةٌ عبر EF تحديداً: المسار الذي كان يفلت من الترجمة.
    private static Task ReadAsync(AppDbContext db, int productId) =>
        db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Id).ToListAsync();
}
