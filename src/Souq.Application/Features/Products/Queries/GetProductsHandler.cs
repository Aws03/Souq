using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Contracts;

namespace Souq.Application.Features.Products.Queries;

// معالج الاستعلام: يترجم الطلب إلى معايير بحث مطبوعة ويمرّرها لمنفذ القراءة مع لغة المتجر الافتراضية. لا يعرف
// SQL ولا EF — الإسقاط والترتيب الحتمي في CatalogQueries (Infrastructure، ADR-0008).
public class GetProductsHandler : IRequestHandler<GetProductsQuery, ProductSearchPage>
{
    private readonly ICatalogQueries _catalog;
    private readonly ITenantContext _tenant;
    private readonly ISearchLog _searchLog;
    private readonly ILogger<GetProductsHandler> _logger;

    public GetProductsHandler(
        ICatalogQueries catalog, ITenantContext tenant, ISearchLog searchLog, ILogger<GetProductsHandler> logger)
    {
        _catalog = catalog; _tenant = tenant; _searchLog = searchLog; _logger = logger;
    }

    public async Task<ProductSearchPage> Handle(GetProductsQuery q, CancellationToken ct)
    {
        var culture = _tenant.RequireTenant().DefaultCulture;
        var page = await _catalog.SearchProductsAsync(
            new ProductSearch(q.Keyword, q.CategoryIds, q.MinPrice, q.MaxPrice, q.SortBy, q.OnSale, q.Exact),
            PageRequest.From(q), culture, ct);

        // ============================================================================
        // يُسجَّل **بعد** معرفة النتيجة، وقبل الإرجاع بلا `await` (M13):
        //
        //   • بعد النتيجة لأنّ السطر الذي يستحقّ تصرّفاً هو "بحثٌ لم يجد شيئاً"، و`TotalCount` وحده يقوله.
        //     وهو العدد الكلّي لا عدد هذه الصفحة: صفحةٌ ثالثة فارغة من نتائجٍ كثيرة ليست بحثاً فاشلاً.
        //   • والتسجيل لا يُنتظر لأنّ `Record` تُودع في ذاكرةٍ وتعود — لا تلمس قاعدةً ولا ترمي. فلا يُضيف
        //     إلى زمن البحث شيئاً يُقاس، وهو الشرط الذي فرضته المرحلة على نفسها.
        //
        // ولا يُسجَّل إلا بحثٌ بكلمة: التصفّح بالفئة أو السعر ليس استعلاماً، وسطرٌ فارغ لا يخبر التاجر بشيء
        // (والكيان نفسه يرفضه، فهذا الشرط للوضوح لا للحماية).
        // ============================================================================
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            // ============================================================================
            // الحراسة هنا **رغم** أنّ العقد يقول "لا يرمي أبداً"، ولأنّ العقد يقوله:
            //
            // "بحثُ المتسوّق لا يفشل بسبب قياس" خاصيّةُ منتَجٍ لا تفصيلُ تنفيذ، ووجوبها لا يتعلّق بأيّ
            // تنفيذٍ رُكِّب. تركُها لانضباط `SearchLogBuffer` وحده يعني أنّ تنفيذاً ثانياً يوماً — أو بديلاً
            // في اختبار — يستطيع إسقاط نتائج المتسوّق، وهذه آخر نقطة يمكن منعُه فيها.
            //
            // والاستثناء يُسجَّل لا يُبتلع صامتاً: هذه الحراسة تحمي البحث، ولا يجوز أن تُخفي عيباً في السجلّ.
            // ============================================================================
            try
            {
                _searchLog.Record(q.Keyword, page.TotalCount, culture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search log rejected a term; results returned unaffected");
            }
        }

        return page;
    }
}
