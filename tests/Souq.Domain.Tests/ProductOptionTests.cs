using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// خيارات المنتج ومتغيّراته كتركيبات (P-08a، ADR-0040، ProductVariants.md V2). كل قاعدة في Product.SetOptions وAddVariant لها
// اختبار هنا: الحدود، التفرّد بكل لغة، التركيبة الكاملة والفريدة، القيمة المستخدمة لا تُحذف، تحويل المنتج البسيط، والفشل الذرّي.
public class ProductOptionTests
{
    private int _nextId = 100;

    [Fact]
    public void الخيار_الأول_يحوّل_المتغيّر_الافتراضي_نفسه_بلا_إعادة_إنشاء()
    {
        var product = NewProduct();
        var original = product.DefaultVariant;

        product.SetOptions([Option("المقاس", ["S", "M", "L"], existing: 1)]);
        Saved(product);

        product.Variants.Should().ContainSingle().Which.Should().BeSameAs(original);
        original.CombinationKey.Should().NotBeNull();
        product.VariantLabel(original, "ar").Should().Be("M");
        product.ImplicitVariant.Should().BeSameAs(original, "متغيّر نشط واحد يبقى ضمنياً — كل عميل قديم يعمل");
    }

    [Fact]
    public void منتج_بلا_خيارات_لا_وصف_لمتغيّره_ولا_مفتاح_ولا_يقبل_متغيّراً_ثانياً()
    {
        var product = NewProduct();

        product.HasOptions.Should().BeFalse();
        product.DefaultVariant.CombinationKey.Should().BeNull();
        product.VariantLabel(product.DefaultVariant, "ar").Should().BeNull();
        Code(() => product.AddVariant([1], Jod(10))).Should().Be("OptionsRequired");
    }

    [Fact]
    public void حتى_3_خيارات_و20_قيمة_وقيمة_واحدة_على_الأقل()
    {
        var product = NewProduct();

        Code(() => product.SetOptions([Option("أ", ["1"], 0), Option("ب", ["1"], 0), Option("ج", ["1"], 0), Option("د", ["1"], 0)]))
            .Should().Be("TooManyOptions");
        Code(() => product.SetOptions([Option("المقاس", Enumerable.Range(1, 21).Select(i => $"v{i}").ToArray(), 0)]))
            .Should().Be("TooManyOptionValues");
        Code(() => product.SetOptions([Option("المقاس", [], 0)])).Should().Be("OptionValuesRequired");

        product.SetOptions([Option("أ", ["1"], 0), Option("ب", ["1"], 0),
            Option("ج", Enumerable.Range(1, 20).Select(i => $"v{i}").ToArray(), 0)]);
        product.Options.Should().HaveCount(3);
    }

    [Fact]
    public void حتى_100_متغيّر_والمعطّل_منها_يُعدّ()
    {
        var product = NewProduct();
        product.SetOptions([Option("المقاس", Enumerable.Range(1, 20).Select(i => $"S{i}").ToArray(), 0),
            Option("اللون", Enumerable.Range(1, 6).Select(i => $"C{i}").ToArray(), 0)]);
        Saved(product);
        var sizes = Values(product, 0);
        var colours = Values(product, 1);

        var combinations = sizes.SelectMany(s => colours.Select(c => new[] { s.Id, c.Id }))
            .Where(ids => !(ids[0] == sizes[0].Id && ids[1] == colours[0].Id)).Take(99).ToList();
        foreach (var ids in combinations) product.AddVariant(ids, Jod(10), isActive: false);

        product.Variants.Should().HaveCount(Product.MaxVariants);
        Code(() => product.AddVariant([sizes[19].Id, colours[5].Id], Jod(10))).Should().Be("TooManyVariants");
    }

    [Fact]
    public void أسماء_الخيارات_والقيم_فريدة_بكل_لغة_بلا_اعتبار_لحالة_الأحرف()
    {
        var product = NewProduct();

        Code(() => product.SetOptions([Option("Size", ["S"], 0, en: "Size"), Option("Colour", ["Red"], 0, en: "size")]))
            .Should().Be("DuplicateOptionName");
        Code(() => product.SetOptions([Option("المقاس", ["S", " s "], 0)])).Should().Be("DuplicateOptionValue");

        // القيمة نفسها في خيارين مختلفين مقبولة (مثلاً "أخرى")، والاسم نفسه بلغتين مختلفتين ليس تكراراً.
        product.SetOptions([Option("المقاس", ["أخرى"], 0), Option("اللون", ["أخرى"], 0)]);
        product.Options.Should().HaveCount(2);
    }

    [Fact]
    public void الأسماء_مطلوبة_محدودة_بلغات_مدعومة_وبلا_محارف_تحكّم()
    {
        var product = NewProduct();

        Code(() => product.SetOptions([Option("   ", ["S"], 0)])).Should().Be("OptionNameRequired");
        Code(() => product.SetOptions([Option(new string('م', 51), ["S"], 0)])).Should().Be("OptionNameTooLong");
        Code(() => product.SetOptions([Option("المقاس", ["S\nL"], 0)])).Should().Be("OptionNameInvalid");
        Code(() => product.SetOptions([new ProductOptionDefinition(null, new Dictionary<string, string> { ["fr"] = "Taille" },
            [Value("S")], 0)])).Should().Be("OptionNameRequired");

        product.SetOptions([Option(" المقاس ", [" S "], 0, en: " Size ")]);
        product.Options.Single().NameIn("en").Should().Be("Size");
        product.Options.Single().Values.Single().NameIn("ar").Should().Be("S");
    }

    [Fact]
    public void خيار_جديد_يسمّي_قيمة_المتغيّرات_القائمة_صراحةً()
    {
        var product = NewProduct();

        Code(() => product.SetOptions([new ProductOptionDefinition(null, Names("المقاس"), [Value("S")])]))
            .Should().Be("ExistingVariantsValueRequired");
        Code(() => product.SetOptions([Option("المقاس", ["S", "M"], existing: 2)])).Should().Be("ExistingVariantsValueRequired");
    }

    [Fact]
    public void المتغيّر_يحتاج_قيمة_واحدة_من_كل_خيار_وتركيبة_غير_مستخدمة()
    {
        var product = TwoOptionProduct();
        var (sizes, colours) = (Values(product, 0), Values(product, 1));

        Code(() => product.AddVariant([sizes[1].Id], Jod(10))).Should().Be("IncompleteVariantCombination");
        Code(() => product.AddVariant([sizes[0].Id, sizes[1].Id], Jod(10))).Should().Be("IncompleteVariantCombination");
        Code(() => product.AddVariant([sizes[0].Id, colours[0].Id], Jod(10))).Should().Be("DuplicateVariantCombination",
            "الافتراضي يحمل S / أحمر");
        Code(() => product.AddVariant([colours[1].Id, sizes[0].Id, 99999], Jod(10))).Should().Be("OptionValueNotFound");

        var variant = product.AddVariant([colours[1].Id, sizes[1].Id], Jod(12), sku: "tee-m-blue");
        product.VariantLabel(variant, "ar").Should().Be("M / أزرق", "الترتيب ترتيب الخيارات لا ترتيب المعرّفات المرسلة");
        product.VariantLabel(variant, "en").Should().Be("M / Blue");
        Code(() => product.AddVariant([sizes[1].Id, colours[1].Id], Jod(10))).Should().Be("DuplicateVariantCombination");
        Code(() => product.AddVariant([sizes[2].Id, colours[1].Id], Jod(10), sku: "TEE-M-BLUE")).Should().Be("DuplicateVariantSku");
        product.Variants.Should().HaveCount(2, "الرفض لا يضيف شيئاً");
    }

    [Fact]
    public void قيمة_منتج_آخر_لا_تُستخدم_هنا()
    {
        var product = TwoOptionProduct();
        var other = TwoOptionProduct();

        Code(() => product.AddVariant([Values(other, 0)[1].Id, Values(other, 1)[1].Id], Jod(10))).Should().Be("OptionValueNotFound");
        Code(() => product.SetOptions([new ProductOptionDefinition(other.Options.First().Id, Names("المقاس"), [Value("S")])]))
            .Should().Be("OptionNotFound");
    }

    [Fact]
    public void قيمة_يستخدمها_متغيّر_ولو_معطّلاً_لا_تُحذف_وغير_المستخدمة_تُحذف()
    {
        var product = TwoOptionProduct();
        var (sizes, colours) = (Values(product, 0), Values(product, 1));
        var medium = product.AddVariant([sizes[1].Id, colours[0].Id], Jod(10));
        Saved(product);
        product.DeactivateVariant(medium.Id);

        Code(() => product.SetOptions(Keep(product, dropValue: sizes[1].Id))).Should().Be("OptionValueInUse");

        product.SetOptions(Keep(product, dropValue: sizes[2].Id));
        Values(product, 0).Select(v => v.NameIn("ar")).Should().Equal("S", "M");
    }

    [Fact]
    public void إعادة_تسمية_قيمة_مستخدمة_تغيّر_الوصف_لا_المفتاح()
    {
        var product = TwoOptionProduct();
        var key = product.DefaultVariant.CombinationKey;
        var definitions = Keep(product);
        definitions[1] = definitions[1] with
        {
            Values = [Value("قرمزي", en: "Crimson", id: Values(product, 1)[0].Id), .. definitions[1].Values.Skip(1)],
        };

        product.SetOptions(definitions);

        product.VariantLabel(product.DefaultVariant, "en").Should().Be("S / Crimson");
        product.DefaultVariant.CombinationKey.Should().Be(key);
    }

    [Fact]
    public void إعادة_ترتيب_الخيارات_والقيم_تغيّر_الوصف_والمواضع_لا_مفتاح_التركيبة()
    {
        var product = TwoOptionProduct();
        var variant = product.AddVariant([Values(product, 0)[2].Id, Values(product, 1)[1].Id], Jod(10));
        var keys = product.Variants.Select(v => v.CombinationKey).ToList();

        var definitions = Keep(product);
        product.SetOptions([definitions[1], definitions[0] with { Values = [.. definitions[0].Values.Reverse()] }]);

        product.Variants.Select(v => v.CombinationKey).Should().Equal(keys);
        product.VariantLabel(variant, "ar").Should().Be("أزرق / L");
        Values(product, 1).Select(v => v.NameIn("ar")).Should().Equal("L", "M", "S");
    }

    [Fact]
    public void خيار_ثانٍ_لمنتج_بمتغيّرات_يُسند_قيمته_لكلّها_وتبقى_التركيبات_فريدة()
    {
        var product = NewProduct();
        product.SetOptions([Option("المقاس", ["S", "M"], 0)]);
        Saved(product);
        var medium = product.AddVariant([Values(product, 0)[1].Id], Jod(12));
        Saved(product);

        product.SetOptions([.. Keep(product), Option("اللون", ["أحمر", "أزرق"], existing: 1)]);

        product.Variants.Select(v => product.VariantLabel(v, "ar")).Should().BeEquivalentTo(["S / أزرق", "M / أزرق"]);
        product.Variants.Select(v => v.CombinationKey).Should().OnlyHaveUniqueItems();
        medium.OptionValues.Should().HaveCount(2);
    }

    [Fact]
    public void حذف_خيار_يُرفض_إن_تطابقت_بعده_تركيبتان_ويُقبل_إن_بقيت_فريدة()
    {
        var product = TwoOptionProduct();
        var (sizes, colours) = (Values(product, 0), Values(product, 1));
        product.AddVariant([sizes[0].Id, colours[1].Id], Jod(10));

        Code(() => product.SetOptions([Keep(product)[0]])).Should().Be("OptionRemovalCollides", "S / أحمر وS / أزرق يصبحان S مرّتين");

        product.SetOptions([Keep(product)[1]]);
        product.Variants.Select(v => product.VariantLabel(v, "ar")).Should().BeEquivalentTo(["أحمر", "أزرق"]);
        product.Options.Should().ContainSingle();
    }

    [Fact]
    public void حذف_كل_الخيارات_يعيد_المنتج_بسيطاً_إن_بقي_له_متغيّر_واحد_فقط()
    {
        var single = TwoOptionProduct();
        single.SetOptions([]);
        single.HasOptions.Should().BeFalse();
        single.DefaultVariant.CombinationKey.Should().BeNull();
        single.DefaultVariant.OptionValues.Should().BeEmpty();

        var multi = TwoOptionProduct();
        multi.AddVariant([Values(multi, 0)[1].Id, Values(multi, 1)[0].Id], Jod(10), isActive: false);
        Code(() => multi.SetOptions([])).Should().Be("OptionRemovalCollides", "المتغيّرات لا تُحذف، والمعطّل منها يُعدّ");
    }

    [Fact]
    public void التعريف_المرفوض_لا_يغيّر_شيئاً()
    {
        var product = TwoOptionProduct();
        var (sizes, colours) = (Values(product, 0), Values(product, 1));
        product.AddVariant([sizes[1].Id, colours[0].Id], Jod(10));
        var definitions = Keep(product, dropValue: sizes[1].Id);
        definitions[1] = definitions[1] with { Names = Names("اللون الجديد") };

        Code(() => product.SetOptions(definitions)).Should().Be("OptionValueInUse");

        product.Options.OrderBy(o => o.Position).Last().NameIn("ar").Should().Be("اللون");
        Values(product, 0).Should().HaveCount(3);
    }

    [Fact]
    public void الخيار_والقيمة_بمعرّفاتهما_تحت_أصحابهما_فقط_ولا_يُذكران_مرّتين()
    {
        var product = TwoOptionProduct();
        var definitions = Keep(product);

        Code(() => product.SetOptions([definitions[0], definitions[0] with { Names = Names("آخر") }])).Should().Be("OptionNotFound");
        Code(() => product.SetOptions([definitions[0] with { Values = [.. definitions[0].Values, definitions[1].Values[0] with { Names = Names("X") }] },
            definitions[1]])).Should().Be("OptionValueNotFound", "قيمة لا تنتقل من خيار لآخر");
    }

    [Fact]
    public void الوصف_بلغة_مطلوبة_وإلا_أول_لغة_متاحة()
    {
        var product = NewProduct();
        product.SetOptions([new ProductOptionDefinition(null, Names("المقاس", en: "Size"),
            [Value("كبير", en: "Large"), Value("صغير")], ExistingVariantsValue: 1)]);

        product.VariantLabel(product.DefaultVariant, "en").Should().Be("صغير", "لا اسم إنجليزي لهذه القيمة");
        product.VariantLabel(product.DefaultVariant.Id, "ar").Should().Be("صغير");
        product.VariantLabel(424242, "ar").Should().BeNull("متغيّر ليس من هذا المنتج لا وصف له");
    }

    [Fact]
    public void تجميع_الوصف_يتجاهل_الفارغ_ويختار_اللغة_ثم_الترتيب_الثابت()
    {
        VariantLabels.Compose([], "ar").Should().BeNull();
        VariantLabels.Compose([new Dictionary<string, string> { ["en"] = "M", ["ar"] = "م" }, new Dictionary<string, string> { ["en"] = "Red" }], "ar")
            .Should().Be("م / Red");
    }

    // ── مساعدات ──────────────────────────────────────────────────────────────

    private Product NewProduct()
    {
        var product = new Product($"tee-{Guid.NewGuid():N}"[..12], categoryId: 1,
            new Dictionary<string, CatalogText> { ["ar"] = new("قميص") }, Jod(10));
        WithId(product.DefaultVariant, ++_nextId);
        return product;
    }

    // مقاس (S, M, L) ولون (أحمر، أزرق) — الافتراضي "S / أحمر".
    private Product TwoOptionProduct()
    {
        var product = NewProduct();
        product.SetOptions([Option("المقاس", ["S", "M", "L"], 0, en: "Size"),
            new ProductOptionDefinition(null, Names("اللون", en: "Colour"), [Value("أحمر", en: "Red"), Value("أزرق", en: "Blue")], 0)]);
        Saved(product);
        return product;
    }

    private void Saved(Product product)
    {
        foreach (var entity in product.Options.Cast<Entity>()
                     .Concat(product.Options.SelectMany(o => o.Values))
                     .Concat(product.Variants)
                     .Where(e => e.Id == 0))
            WithId(entity, ++_nextId);
    }

    private static List<ProductOptionValue> Values(Product product, int position) =>
        product.Options.Single(o => o.Position == position).Values.OrderBy(v => v.Position).ToList();

    // التعريف الحالي كما هو (بمعرّفاته)، مع حذف قيمة اختيارياً.
    private static List<ProductOptionDefinition> Keep(Product product, int? dropValue = null) =>
        product.Options.OrderBy(o => o.Position).Select(o => new ProductOptionDefinition(o.Id, NamesOf(o.Translations),
            o.Values.OrderBy(v => v.Position).Where(v => v.Id != dropValue)
                .Select(v => new ProductOptionValueDefinition(v.Id, NamesOf(v.Translations))).ToList())).ToList();

    private static Dictionary<string, string> NamesOf(IEnumerable<OptionTranslation> translations) =>
        translations.ToDictionary(t => t.Culture, t => t.Name);

    private static ProductOptionDefinition Option(string name, string[] values, int? existing, string? en = null) =>
        new(null, Names(name, en), values.Select(v => Value(v)).ToList(), existing);

    private static ProductOptionValueDefinition Value(string ar, string? en = null, int? id = null) => new(id, Names(ar, en));

    private static Dictionary<string, string> Names(string ar, string? en = null)
    {
        var names = new Dictionary<string, string> { ["ar"] = ar };
        if (en is not null) names["en"] = en;
        return names;
    }

    private static Money Jod(decimal amount) => new(amount, "JOD");

    private static string Code(Action action)
    {
        try { action(); }
        catch (InvalidProductVariantException ex) { return ex.Code; }
        throw new InvalidOperationException("متوقَّع رفض برمز قاعدة متغيّرات");
    }

    private static T WithId<T>(T entity, int id) where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }
}
