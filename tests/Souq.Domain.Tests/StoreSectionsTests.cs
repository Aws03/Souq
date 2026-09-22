using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Domain.Tests;

// ============================================================================
// أقسام الصفحة الرئيسية (C8، ADR-0060).
//
// ما يُحرس هنا ليس أنّ الترتيب يُحفظ — بل الأربعةُ التي يقع على كلٍّ منها ضررٌ في متجرٍ حقيقيّ:
//   • أنّ **متجراً لم يضبط أقسامه لا تتغيّر رئيسيتُه** عند الترقية،
//   • أنّ **الكتالوج لا يُطفأ** بأيّ مدخل، فالرئيسيةُ بدونه بلا منتجات،
//   • أنّ **نوعاً مجهولاً يُرفض** ولا يُتجاهَل بصمت،
//   • وأنّ **نوعاً جديداً يُلحَق مُطفأً**، فلا يظهر في رئيسيةِ متجرٍ لم يطلبه.
// ============================================================================
public class StoreSectionsTests
{
    private static List<StoreSection> All(params (string Type, bool Enabled)[] overrides)
    {
        var map = StoreSections.Types.ToDictionary(t => t, _ => true, StringComparer.Ordinal);
        foreach (var (type, enabled) in overrides) map[type] = enabled;
        return [.. StoreSections.Types.Select(t => new StoreSection(t, map[t]))];
    }

    [Fact]
    public void المتجر_الذي_لم_يضبط_أقسامه_يأخذ_ترتيب_الصفحة_كما_هي()
    {
        var sections = StoreSections.Default;

        sections.Items.Select(i => i.Type).Should().Equal(StoreSections.Types);
        sections.Items.Should().OnlyContain(i => i.Enabled);
        sections.EnabledTypes.Should().Equal(StoreSections.Types);
    }

    // لا شيء ⇒ الافتراضي. هذا ما يجعل الترقية بلا أثر: مستندٌ قديمٌ بلا حقلٍ يقرأ الرئيسية نفسها.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void لا_مدخل_يعني_الافتراضي(bool nullInput)
    {
        var sections = StoreSections.Create(nullInput ? null : []);

        sections.Items.Should().BeEquivalentTo(StoreSections.Default.Items, o => o.WithStrictOrdering());
    }

    [Fact]
    public void الترتيب_يُحفظ_كما_أُرسل()
    {
        var reversed = All().AsEnumerable().Reverse().ToList();

        StoreSections.Create(reversed).Items.Select(i => i.Type)
            .Should().Equal(reversed.Select(i => i.Type));
    }

    [Fact]
    public void القسم_المُطفأ_يبقى_في_القائمة_ويغيب_عن_المرسوم()
    {
        var sections = StoreSections.Create(All((StoreSections.Offers, false)));

        sections.Items.Should().Contain(i => i.Type == StoreSections.Offers && !i.Enabled);
        sections.EnabledTypes.Should().NotContain(StoreSections.Offers);
        sections.EnabledTypes.Should().Contain(StoreSections.Catalog);
    }

    // رئيسيةٌ بلا كتالوج رئيسيةٌ بلا منتجات — وهي أوّلُ شاشةٍ يصلها الزائر.
    [Fact]
    public void الكتالوج_لا_يُطفأ()
    {
        var act = () => StoreSections.Create(All((StoreSections.Catalog, false)));

        act.Should().Throw<InvalidTenantOperationException>().WithMessage($"*{StoreSections.Catalog}*");
    }

    [Theory]
    [InlineData("carousel")]
    [InlineData("")]
    [InlineData("  ")]
    public void النوع_المجهول_يُرفض_ولا_يُتجاهَل(string type)
    {
        var act = () => StoreSections.Create([new StoreSection(type, true)]);

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void تكرار_النوع_يُرفض()
    {
        var act = () => StoreSections.Create(
            [new StoreSection(StoreSections.Hero, true), new StoreSection(StoreSections.Hero, true)]);

        act.Should().Throw<InvalidTenantOperationException>();
    }

    // ما لم يُذكر يُلحَق **مُطفأً**: نوعٌ يُضاف في إصدارٍ قادم لا يظهر في رئيسيةِ متجرٍ ضبط
    // أقسامه ولم يطلبه — المبدأ نفسه الذي يحكم وحدات المتجر.
    [Fact]
    public void ما_لم_يُذكر_يُلحَق_مُطفأً_إلا_ما_لا_يُطفأ()
    {
        var sections = StoreSections.Create([new StoreSection(StoreSections.Hero, true)]);

        sections.Items.Select(i => i.Type).Should().BeEquivalentTo(StoreSections.Types);
        sections.EnabledTypes.Should().Equal([StoreSections.Hero, StoreSections.Catalog]);
    }

    [Fact]
    public void حالة_الأحرف_لا_تصنع_نوعاً_ثانياً()
    {
        var sections = StoreSections.Create([new StoreSection("HERO", true)]);

        sections.Items[0].Type.Should().Be(StoreSections.Hero);
    }

    // الضبطُ يمرّ بالتجمّع، و`null` تعني «لم أذكرها» فلا تمحو تخطيطاً حفظه التاجر.
    [Fact]
    public void التجمّع_يحفظ_الأقسام_ولا_يمحوها_بلا_مدخل()
    {
        var tenant = new Tenant("متجر تجريبي", "demo-store", "JOD", "ar", "Asia/Amman");
        var chosen = StoreSections.Create(All((StoreSections.Featured, false)));

        tenant.UpdateSections(chosen);
        tenant.Settings.Sections.EnabledTypes.Should().NotContain(StoreSections.Featured);

        tenant.UpdateSections(null);
        tenant.Settings.Sections.EnabledTypes.Should().NotContain(StoreSections.Featured);
    }
}

// ============================================================================
// تسمياتُ المتجر (C8، ADR-0062).
//
// **القائمةُ المغلقة هي الاختبار كلُّه.** ملفُّ الترجمة يحمل — إلى جانب نصوص العرض — رسائلَ
// الأخطاء ونصوصَ الإتاحة التي لا يقرؤها إلّا قارئُ الشاشة. تاجرٌ يُعيد كتابة «تعذّر إتمام
// الدفع» أو يُفرغ عنوان زرٍّ لا يراه إلّا الأعمى لا يُخصّص متجره — يكسره، وقد يكسره على مَن لا
// حيلة له.
// ============================================================================
public class StoreTextOverridesTests
{
    private static Dictionary<string, IReadOnlyDictionary<string, string?>?> One(string key, string text) =>
        new() { [key] = new Dictionary<string, string?> { ["ar"] = text } };

    [Fact]
    public void مفتاح_مسموح_يُحفظ_بلغته()
    {
        var texts = StoreTextOverrides.Create(One("store.newArrivals", "مجموعاتنا"));

        texts.Values["store.newArrivals"]["ar"].Should().Be("مجموعاتنا");
    }

    [Theory]
    [InlineData("errors.connection")]
    [InlineData("nav.openMenu")]
    [InlineData("storeClosed.suspended.title")]
    [InlineData("")]
    public void مفتاح_خارج_القائمة_يُرفض_ولا_يُتجاهَل(string key)
    {
        var act = () => StoreTextOverrides.Create(One(key, "نصّ"));

        act.Should().Throw<InvalidTenantOperationException>();
    }

    // الفارغُ حذفٌ لا قيمةٌ فارغة: إفراغُ تسميةٍ يعيد النصّ الأصليّ بدل أن يترك زرّاً بلا كلمة.
    [Fact]
    public void إفراغ_التسمية_يحذفها_ولا_يترك_نصّاً_فارغاً()
    {
        var texts = StoreTextOverrides.Create(One("store.newArrivals", "   "));

        texts.Values.Should().NotContainKey("store.newArrivals");
    }

    [Fact]
    public void نصّ_أطول_من_الحدّ_يُرفض()
    {
        var act = () => StoreTextOverrides.Create(
            One("store.newArrivals", new string('ن', StoreTextOverrides.ValueMaxLength + 1)));

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void لغة_غير_مدعومة_تُرفض()
    {
        var act = () => StoreTextOverrides.Create(new Dictionary<string, IReadOnlyDictionary<string, string?>?>
        {
            ["store.newArrivals"] = new Dictionary<string, string?> { ["fr"] = "Nouveautés" },
        });

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void لا_مدخل_يعني_بلا_تسميات()
    {
        StoreTextOverrides.Create(null).Values.Should().BeEmpty();
    }

    // القائمةُ نصوصُ عرضٍ وحدها: هذا الفحص هو ما يمنع توسيعَها سهواً إلى ما يحمل معنى.
    [Fact]
    public void القائمة_لا_تحوي_رسائل_أخطاء_ولا_نصوص_إتاحة()
    {
        StoreTextOverrides.Allowed.Should().NotContain(k =>
            k.StartsWith("errors.", StringComparison.Ordinal)
            || k.StartsWith("storeClosed.", StringComparison.Ordinal)
            || k.Contains("aria", StringComparison.OrdinalIgnoreCase)
            || k.Contains("Label", StringComparison.Ordinal));
    }
}
