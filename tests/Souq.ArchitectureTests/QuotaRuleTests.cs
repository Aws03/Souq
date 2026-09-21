using System.Reflection;
using AwesomeAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Souq.Domain.Platform;

namespace Souq.ArchitectureTests;

// ============================================================================
// قواعد الحصص كاختبارات (C2، ADR-0049 §الالتزامات). الالتزام الأول هناك يقول حرفياً: "منفذ واحد،
// تنفيذ واحد، واختبار". هذا هو الاختبار.
//
// العلّة التي يحرسها ليست أن حارساً بعينه خاطئ، بل أن **لا شيء يقول القاعدة**: العدّ-ثم-الكتابة
// يفشل مفتوحاً وبصمت وباختبارات خضراء، فالموضع التالي منه لن يُكتشف إلّا يوم يتجاوز متجرٌ حدّه
// ولا أحد يعلم. هنا يُكتشف عند البناء.
// ============================================================================
public class QuotaRuleTests
{
    private static readonly string ApplicationPath = typeof(Souq.Application.DependencyInjection).Assembly.Location;
    private static readonly string InfrastructurePath = typeof(Souq.Infrastructure.DependencyInjection).Assembly.Location;

    // ============================================================================
    // مواضع "عُدَّ ثم اكتب" المعروفة اليوم. القائمة **ليست براءة**: هي إقرار بأن كلاً منها
    // فُحص وعُرف حدّه. الإضافة إليها قرار يُراجَع — والمطلوب في كل حالة جديدة هو ITenantQuotaGuard.
    //
    //   • SearchSynonymCommands و WishlistUseCases — سقفان **ليّنان** سابقان لهذه المرحلة
    //     (200 مرادفاً، 200 أمنية). سباقٌ نادر يُدخل الواحد بعد المئتين، ولا أثر تجاريّاً لذلك:
    //     لا أحد يدفع مقابلهما ولا يُقاسان في خطة. تحويلهما إلى عدّاد يعني صفَّي عدّاد لكل متجر
    //     ومصالحةً لهما مقابل صفرٍ من الصواب المكتسب — رُفض عن قصد، وهذا السطر هو القرار.
    // ============================================================================
    // ونطاقه **الإدخال** لا كل كتابة (ADR-0049: "any other count-then-insert"): `AccountStatusChanger`
    // يَعُدّ ثم **يُعدِّل** ولا يُنشئ شيئاً، فلا يقع هنا — وعلّته ليست هذه القاعدة بل TD-68، وتُغلَق
    // بفحص الإقلاع في `DatabaseIsolation`. ولو وُسِّع الشرط ليشمل `SaveChangesAsync` لاصطاد كل
    // معالج يَعُدّ شيئاً، ولفقدت القائمة معناها في أوّل يوم.
    // ============================================================================
    private static readonly HashSet<string> ReviewedCountThenWrite = new(StringComparer.Ordinal)
    {
        "Souq.Application.Features.Products.Commands.CreateSearchSynonymHandler",
        "Souq.Application.Features.Wishlist.AddToWishlistHandler",
        // ============================================================================
        // كشفه هذا الفحص عند إدخاله، ولم يكن في أي جرد — وهو **سليم**، وهذا سبب بقائه هنا لا
        // إصلاحه. ADR-0049 يسمّي صنفه صراحةً: "الحدود داخل التجمّع محميّة بـ rowversion الجذر".
        //
        // العدّ (`CountActiveAsync`: كم مرّة استعمل هذا العميلُ هذا الكوبون) يُغذّي `coupon.Redeem`،
        // والكوبون تجمّعٌ عليه rowversion يُكتب في الحفظ نفسه. فأيّ استعمالٍ متزامن لنفس الكوبون
        // يكتب صفّه أيضاً، فيخسر أحدهما التعارض ويُعاد من قراءةٍ جديدة فيَعُدّ الحقيقة الملتزمة.
        // الحدّ هنا محميّ بالجذر لا بالعدّ، ولا يحتاج عدّاداً ولا يستفيد منه.
        // ============================================================================
        "Souq.Application.Features.Coupons.Redemptions.CouponRedemptions",
        // التنفيذ الوحيد للحارس نفسه: يَعُدّ عند إنشاء صفّ العدّاد وعند المصالحة، وكلاهما تحت قفل.
        "Souq.Infrastructure.Persistence.TenantQuotaGuard",
    };

    // ============================================================================
    // بالبادئة لا بقائمة أسماء، وهذا درسٌ دفعه الفحص الذاتي أدناه ثمنَه فوراً: القائمة المغلقة
    // (`Count`, `CountAsync`, …) كانت **تفوّت** `CountForCustomerAsync` في حارس الأمنيات
    // و`CountActiveByRoleAsync` في حارس آخر مدير — أي أنّ الفحص كان سيمرّ أخضر وهو أعمى عن
    // موضعين من ثلاثة. المستودعات هنا تسمّي عدّاداتها بأسماء مجالها، لا بأسماء LINQ.
    // ============================================================================
    private static bool IsCount(string name) =>
        name.StartsWith("Count", StringComparison.Ordinal)
        || name.StartsWith("LongCount", StringComparison.Ordinal);

    private static readonly HashSet<string> WriteMethods = new(StringComparer.Ordinal)
    {
        "Add", "AddAsync", "AddRange", "AddRangeAsync",
    };

    // ============================================================================
    // النداء يخصّ التخزين لا الذاكرة. بغير هذا الشرط اصطاد الفحص `StoreReportQueries`: يَعُدّ طلبات
    // المتجر للتقرير ثم يستدعي `points.Add(...)` على `List<T>` — لا علاقة له بإدخال صفّ، ولا سباق
    // فيه أصلاً. إبقاؤه كان سيعني قائمة مراجَعة مليئة بالأبرياء، وقائمةٌ كهذه لا يقرؤها أحد.
    //
    // فالمقبول مصدران: منفذ مستودع من مجال Souq، أو EF Core مباشرةً.
    // ============================================================================
    private static bool IsPersistence(MethodReference reference) =>
        reference.DeclaringType?.FullName is { } declaring
        && (declaring.StartsWith("Souq.", StringComparison.Ordinal)
            || declaring.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

    // ============================================================================
    // طريقةٌ واحدة تَعُدّ ثم تكتب = سباقٌ بين العدّ والكتابة. لا نافذة بينهما تُغلَق بمراجعة: إمّا
    // قفلٌ صريح (ITenantQuotaGuard) وإمّا الحدّ ليس حدّاً.
    //
    // الفحص على مستوى **الطريقة** لا الصنف عمداً: صنفٌ فيه طريقة تَعُدّ وأخرى تكتب ليس فيه سباق،
    // وتوسيعُ الفحص إليه كان سيملأ القائمة بأبرياء حتى تفقد معناها.
    // ============================================================================
    [Fact]
    public void العدّ_ثم_الكتابة_محصور_في_المواضع_المراجَعة()
    {
        var found = MethodsThatCountThenWrite().ToHashSet(StringComparer.Ordinal);

        // ============================================================================
        // **الفحص يفحص نفسه أوّلاً**، وهذا ليس احتياطاً زائداً: ADR-0049 §الالتزام الرابع كُتب
        // لأن `LastAdministratorConcurrencyTests` كان يمرّ بالحساب ولا يبلغ الفرع الذي يحرسه —
        // "يمرّ ولو حُذف الحارس كلّه". القاعدة القائمة على قائمةٍ مراجَعة تُصاب بالعلّة نفسها
        // بصمت: لو كفّ الكشف عن العمل (تغيّر اسم طريقة، أُلغي التضمين، لم يُقرأ التجميع) لصارت
        // المجموعة فارغة — و"الفارغة ⊆ المراجَعة" خضراءُ إلى الأبد وهي لا تقيس شيئاً.
        //
        // فالمواضع المعروفة **يجب أن تُكتشف**. يومَ يُصلَح أحدها فعلاً (تحوّل إلى ITenantQuotaGuard)
        // يُحذف سطره من القائمة أعلاه، فيبقى الشرطان متّسقين.
        // ============================================================================
        ReviewedCountThenWrite.Except(found, StringComparer.Ordinal).Should().BeEmpty(
            "الكشف نفسه توقّف عن العمل: موضعٌ مراجَع لم يعد يُرى، فالفحص أخضر وهو لا يقيس شيئاً");

        var offenders = found.Where(type => !ReviewedCountThenWrite.Contains(type)).ToList();

        offenders.Should().BeEmpty(
            "العدّ ثم الكتابة يفشل **مفتوحاً** تحت READ_COMMITTED_SNAPSHOT بلا خطأ ولا اختبار أحمر "
            + "(TD-68، ADR-0049). الحدّ التجاري يمرّ بـ ITenantQuotaGuard؛ وسقفٌ ليّن يُضاف هنا بقرار مكتوب");
    }

    // ============================================================================
    // اسمٌ في كتالوج الحدود بلا قاعدة عدّ خلفه هو **حدٌّ يُباع ولا يُفرَض**: تحمله خطة، ويُعرض على
    // الشاشة، ولا يمنع شيئاً — ولا شيء كان سيقول ذلك. الكتالوج تعهّد، وهذا ما يجعله كذلك.
    //
    // انعكاسياً لأن QuotaResources داخلي في Infrastructure — والاقتران بالاسم هو ما تفعله بقيّة
    // قواعد هذا المشروع أصلاً (ReviewedBulkWrites وأخواتها).
    // ============================================================================
    [Fact]
    public void كل_اسم_حدّ_له_قاعدة_عدّ()
    {
        var resources = typeof(Souq.Infrastructure.DependencyInjection).Assembly
            .GetType("Souq.Infrastructure.Persistence.QuotaResources", throwOnError: true)!;
        var counted = (IReadOnlyCollection<string>)resources
            .GetProperty("Counted", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        LimitNames.All.Except(counted, StringComparer.Ordinal).Should().BeEmpty(
            "حدٌّ بلا قاعدة عدّ يُحمَل في الخطة ولا يُفرَض أبداً — أضف قاعدته في QuotaResources");

        // والعكس أيضاً: قاعدة عدّ لاسم خارج الكتالوج شيفرةٌ ميتة تُوهِم أن الحدّ مدعوم.
        counted.Except(LimitNames.All, StringComparer.Ordinal).Should().BeEmpty(
            "قاعدة عدّ لاسم ليس في LimitNames لا يصل إليها شيء");
    }

    // ============================================================================
    // تنفيذ واحد للمنفذ. تنفيذٌ ثانٍ يعني قاعدة عدّ ثانية وسلوكاً ثانياً تحت التزامن، وهو بالضبط
    // ما تمنعه ADR-0049 §الالتزام الأول.
    // ============================================================================
    [Fact]
    public void حارس_الحصص_له_تنفيذ_واحد()
    {
        var implementations = typeof(Souq.Infrastructure.DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(Souq.Application.Features.Billing.Contracts.ITenantQuotaGuard).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        implementations.Should().ContainSingle(
            "منفذ واحد بتنفيذ واحد — والفحص المعماري أعلاه يفترض أن الحارس موضع واحد");
    }

    // الأنواع (العليا) التي فيها **طريقة واحدة** تستدعي عدّاً وكتابةً معاً.
    private static IEnumerable<string> MethodsThatCountThenWrite()
    {
        foreach (var path in new[] { ApplicationPath, InfrastructurePath })
        {
            using var module = ModuleDefinition.ReadModule(path);
            foreach (var type in module.GetTypes())
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    var calls = method.Body.Instructions
                        .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                        .Select(i => i.Operand as MethodReference)
                        .Where(r => r is not null && IsPersistence(r))
                        .Select(r => r!.Name)
                        .ToList();

                    if (calls.Any(IsCount) && calls.Any(WriteMethods.Contains))
                        yield return TopLevel(type).FullName;
                }
        }
    }

    private static TypeDefinition TopLevel(TypeDefinition type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }
}

// ============================================================================
// تكلفة المنتج سرٌّ تجاري (C11). القاعدة: لا حقل تكلفة في أيّ نوع يُسلَّم على **مضيف متجر**.
//
// لماذا اختبار لا مراجعة؟ لأن `PricedLine` — عقد التسعير المشترك — صار يحمل `UnitCost` كي
// تُجمَّد لقطتها على سطر الطلب، وهو يمرّ بخطوة واحدة من `BasketLineDto`. خريطةٌ كسولة (`with`،
// أو نسخٌ بالانعكاس، أو حقل يُضاف "للاكتمال") تكشف للمتسوّق هامشَ متجره. هنا تُكتشف عند البناء.
//
// والاستثناءات مُسمّاة: أنواع الإدارة تحمل التكلفة عن قصد — وهي تُخدَم خلف صلاحية إدارة متجر.
// ============================================================================
public class MerchantCostSecrecyTests
{
    private static readonly System.Reflection.Assembly Application =
        typeof(Souq.Application.DependencyInjection).Assembly;

    // أنواعٌ يراها المتجر (الإدارة) وحده. إضافة اسم هنا قرار يُراجَع، لا سطر يمرّ.
    private static readonly HashSet<string> AdminOnly = new(StringComparer.Ordinal)
    {
        "Souq.Application.Features.Products.Queries.AdminProductDto",
        "Souq.Application.Features.Products.Queries.AdminProductVariantDto",
        "Souq.Application.Features.Products.Commands.CreateProductCommand",
        "Souq.Application.Features.Products.Commands.UpdateProductCommand",
        "Souq.Application.Features.Products.Commands.UpdateProductVariantCommand",
        "Souq.Application.Features.Products.Commands.NewProductVariantInput",
        // عقد التسعير الداخلي: يحمل اللقطة إلى سطر الطلب ولا يُسلَّم كما هو إلى أي عميل.
        "Souq.Application.Features.Baskets.Contracts.PricedLine",
        // تقارير المتجر: الهامش والتكلفة موضوعها، وهي خلف صلاحية store.reports.view.
        "Souq.Application.Features.Reporting.MarginDto",
    };

    // ============================================================================
    // "تكلفة" لا تعني تكلفة بضاعة دائماً. `ShippingOption.Cost` هو **ما يدفعه المتسوّق** للشحن:
    // يُعرض له في السلة ويُجمَّد على طلبه، فإخفاؤه عنه عبث. كشفه الفحص عند إدخاله، وهو أدقّ من
    // تصفيةٍ بالاسم وحده (الحقل اسمه `Cost` مجرّداً، والتمييز في نوعه لا في اسمه).
    // ============================================================================
    private static readonly HashSet<string> NotGoodsCost = new(StringComparer.Ordinal)
    {
        "Souq.Application.Features.Shipping.Contracts.ShippingOption",
        "Souq.Application.Features.Baskets.ShippingOptionDto",
    };

    [Fact]
    public void لا_تكلفة_في_نوعٍ_يراه_متسوّق()
    {
        var offenders = Application.GetTypes()
            .Where(t => t is { IsClass: true } or { IsValueType: true, IsEnum: false })
            .Where(t => (t.Namespace ?? "").StartsWith("Souq.Application.Features", StringComparison.Ordinal))
            .Where(t => !AdminOnly.Contains(t.FullName ?? "") && !NotGoodsCost.Contains(t.FullName ?? ""))
            .Where(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Any(pr => pr.Name.Contains("Cost", StringComparison.Ordinal)
                           // ShippingCost ليست تكلفة بضاعة: مبلغٌ يدفعه المتسوّق ويراه.
                           && !pr.Name.Contains("Shipping", StringComparison.Ordinal)))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty(
            "تكلفة البضاعة سرٌّ تجاري: نوعٌ جديد يحملها يُخدَم لمتسوّق بخطأ واحد. "
            + "إن كان النوع للإدارة وحدها فأضفه إلى AdminOnly بقرار مكتوب");
    }

    // والفحص يفحص نفسه: لو توقّف الكشف عن العمل لبقيت القائمة أعلاه بلا معنى وبقي الاختبار أخضر.
    [Fact]
    public void الكشف_عن_حقول_التكلفة_ما_زال_يعمل()
    {
        var seen = Application.GetTypes()
            .Where(t => AdminOnly.Contains(t.FullName ?? ""))
            .Where(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Any(pr => pr.Name.Contains("Cost", StringComparison.Ordinal)))
            .Select(t => t.FullName)
            .ToList();

        seen.Should().BeEquivalentTo(AdminOnly,
            "كل نوع في قائمة الاستثناء يجب أن يكون **مكتشَفاً** فعلاً — وإلا فالقائمة تحرس لا شيء");
    }
}
