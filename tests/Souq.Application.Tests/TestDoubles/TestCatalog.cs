using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.TestDoubles;

// منتجات وفئات ومخزون اختبار بشكل المرحلتين 5 و6 (نصوص لكل لغة، متغيّر افتراضي، مخزون في وحدة Inventory) — مكان
// واحد بدل تكرار المُنشئات في كل ملف.
public static class TestCatalog
{
    public static Dictionary<string, CatalogText> Texts(string ar, string? en = null)
    {
        var texts = new Dictionary<string, CatalogText> { ["ar"] = new(ar) };
        if (en is not null) texts["en"] = new CatalogText(en);
        return texts;
    }

    public static Dictionary<string, CatalogTextInput> Input(string ar, string? en = null)
    {
        var texts = new Dictionary<string, CatalogTextInput> { ["ar"] = new(ar) };
        if (en is not null) texts["en"] = new CatalogTextInput(en);
        return texts;
    }

    // id يُعطى للمنتج ولمتغيّره الافتراضي معاً (كما بعد الحفظ فعلياً) — أسطر الحجز تحمل معرّف المتغيّر.
    // categoryActive: قابلية البيع تعتمد على الفئة أيضاً (Product.IsSellable، R-07)، ومستودع الكتابة يحمّلها دائماً —
    // فالبديل يحاكي ذلك بفئة مفعّلة افتراضياً.
    public static Product Product(string name = "سماعات", decimal price = 50, int categoryId = 1,
        string currency = "JOD", int? id = null, bool categoryActive = true)
    {
        var product = new Product($"p-{Guid.NewGuid():N}"[..12], categoryId, Texts(name), new Money(price, currency));
        WithCategory(product, TestCatalog.Category($"فئة {categoryId}", $"c-{categoryId}", categoryId, categoryActive));
        if (id is int value)
        {
            WithId(product, value);
            WithId(product.DefaultVariant, value);
        }
        return product;
    }

    public static Category Category(string name, string slug, int? id = null, bool isActive = true)
    {
        var category = new Category(slug, Texts(name));
        if (!isActive) category.Deactivate();
        if (id is int value) WithId(category, value);
        return category;
    }

    // علاقة التنقّل التي يملؤها EF عند التحميل — تُضبط هنا بالمُحدِّد الخاص كما يفعل المستودع.
    public static Product WithCategory(Product product, Category category)
    {
        typeof(Product).GetProperty(nameof(Souq.Domain.Entities.Product.Category))!
            .GetSetMethod(nonPublic: true)!.Invoke(product, [category]);
        return product;
    }

    // مخزون متغيّر بكمية موجودة (حركة التوريد تُهمَل هنا — اختبارات الكيان تغطّيها).
    public static InventoryItem Stock(int onHand, int productId = 1, int variantId = 1, int id = 1, int threshold = 5)
    {
        var item = WithId(new InventoryItem(productId, variantId, threshold), id);
        if (onHand > 0) item.Receive(onHand, Souq.Domain.Enums.StockMovementType.Purchase);
        return item;
    }

    public static T WithId<T>(T entity, int id) where T : Souq.Domain.Common.Entity
    {
        typeof(Souq.Domain.Common.Entity).GetProperty("Id")!.SetValue(entity, id);
        return entity;
    }

    public static ProductDto Dto(int id, string name = "سماعات") => new(
        id, $"p-{id}", name, "وصف", new Dictionary<string, CatalogTextDto> { ["ar"] = new(name, "وصف", null, null) },
        50, null, "JOD", 10, null, null, null, 3, "إلكترونيات", null);
}
