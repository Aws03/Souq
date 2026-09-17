using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Souq.IntegrationTests.Infrastructure;

// ============================================================================
// الخيارات والمتغيّرات عبر نقاط الإدارة (ADR-0040) كما يستعملها التاجر — لا زرع من القاعدة. العقود بشكلها في JSON.
// ============================================================================
public static class VariantAdminApi
{
    public static Dictionary<string, string> Names(string ar, string? en = null)
    {
        var names = new Dictionary<string, string> { ["ar"] = ar };
        if (en is not null) names["en"] = en;
        return names;
    }

    public static object Option(string name, string[] values, int? existing = 0, int? id = null, string? en = null) => new
    {
        id,
        names = Names(name, en),
        values = values.Select(v => new { id = (int?)null, names = Names(v) }).ToArray(),
        existingVariantsValue = existing,
    };

    // التعريف الحالي كما قرأه المدير (بمعرّفاته) — أساس أي تعديل: إعادة تسمية، إضافة قيمة، حذف قيمة.
    public static List<Dictionary<string, object?>> Current(AdminProductBody product) =>
        product.Options.OrderBy(o => o.Position).Select(o => new Dictionary<string, object?>
        {
            ["id"] = o.Id,
            ["names"] = o.Names,
            ["values"] = o.Values.OrderBy(v => v.Position).Select(v => (object)new { id = (int?)v.Id, names = v.Names }).ToList(),
        }).ToList();

    public static Task<HttpResponseMessage> SetOptionsAsync(HttpClient admin, int productId, IEnumerable<object> options) =>
        admin.PutAsJsonAsync($"/api/admin/products/{productId}/options", new { options });

    public static async Task<AdminProductBody> ProductAsync(HttpClient admin, int productId)
    {
        var response = await admin.GetAsync($"/api/admin/products/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AdminProductBody>(TestApi.Json))!;
    }

    public static Task<HttpResponseMessage> CreateVariantsAsync(HttpClient admin, int productId, IEnumerable<object> variants) =>
        admin.PostAsJsonAsync($"/api/admin/products/{productId}/variants", new { variants });

    public static async Task<List<int>> CreateVariantsOkAsync(HttpClient admin, int productId, params object[] variants)
    {
        var response = await CreateVariantsAsync(admin, productId, variants);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdsBody>(TestApi.Json))!.Ids;
    }

    public static int ValueId(AdminProductBody product, string optionAr, string valueAr) =>
        product.Options.Single(o => o.Names["ar"] == optionAr).Values.Single(v => v.Names["ar"] == valueAr).Id;

    public static async Task<(HttpStatusCode Status, string? Code)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    public sealed record IdsBody(List<int> Ids);

    public sealed record AdminProductBody(
        int Id, string Slug, string Status, string? Sku, decimal Price, decimal? CompareAtPrice, string Currency,
        int OnHand, int Reserved, int Available, int LowStockThreshold,
        List<OptionBody> Options, List<VariantBody> Variants, LimitsBody VariantLimits);

    public sealed record OptionBody(int Id, int Position, Dictionary<string, string> Names, List<ValueBody> Values);
    public sealed record ValueBody(int Id, int Position, Dictionary<string, string> Names);

    public sealed record VariantBody(
        int Id, bool IsDefault, bool IsActive, string? Sku, decimal Price, decimal? CompareAtPrice, List<int> OptionValueIds,
        int OnHand, int Reserved, int Available, int LowStockThreshold);

    public sealed record LimitsBody(int MaxOptions, int MaxValuesPerOption, int MaxVariants, int NameMaxLength, int SkuMaxLength);
}
