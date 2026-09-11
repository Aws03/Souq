using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.TestDoubles;

// منتجات وفئات اختبار بشكل المرحلة 5 (نصوص لكل لغة، متغيّر افتراضي) — مكان واحد بدل تكرار المُنشئ في كل ملف.
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

    public static Product Product(string name = "سماعات", decimal price = 50, int stock = 10, int categoryId = 1,
        string currency = "JOD", int? id = null)
    {
        var product = new Product($"p-{Guid.NewGuid():N}"[..12], categoryId, Texts(name), new Money(price, currency), stock);
        if (id is int value) WithId(product, value);
        return product;
    }

    public static Category Category(string name, string slug, int? id = null)
    {
        var category = new Category(slug, Texts(name));
        if (id is int value) WithId(category, value);
        return category;
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
