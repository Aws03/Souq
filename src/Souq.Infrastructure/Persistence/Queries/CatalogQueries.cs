using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Categories.Queries;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// CatalogQueries — تنفيذ ICatalogQueries (ADR-0008): AsNoTracking + إسقاط مباشر إلى صفوف قراءة، ثم DTO في الذاكرة
// (قاموس اللغات، اختيار لغة المتجر). الاسم بلغة مطلوبة وإلا أول لغة. المتجر: النشط في فئة مفعّلة فقط.
//
// السعر المعروض (V3، ADR-0041): أرخص متغيّر **يمكن شراؤه الآن** — نشط وله متاح — لا المتغيّر الافتراضي (P-08b). يُحسب في
// SQL هنا لا في الواجهة: الترتيب والتصفية والعرض يجب أن تتفق على الرقم نفسه، والعميل لا يحسب أسعاراً. لا متغيّر قابلاً
// للشراء ⇒ أرخص متغيّر نشط والمنتج غير متاح. أسعار المعطّلة لا تُحتسب أبداً.
// ============================================================================
internal sealed class CatalogQueries : ICatalogQueries
{
    private readonly AppDbContext _db;
    public CatalogQueries(AppDbContext db) => _db = db;

    // ============================================================================
    // البحث مع استرجاع الخطأ المطبعي (M3، ADR-0042). ثلاث محاولات، بهذا الترتيب:
    //   1. بكلمات المتسوّق كما طبَّعها — وهي المسار الساخن: وُجدت نتائج ⇒ انتهى، بلا أي عمل إضافي.
    //   2. لم يوجد شيء ⇒ تصحيح كل كلمة إلى أقرب كلمة في مفردات الكتالوج بمسافة تحرير محدودة، وإعادة البحث.
    //      نجحت ⇒ تُعاد نتائجها **مع تسمية ما جرى** (SearchedInstead)، لا استبدالاً صامتاً لكلمات المتسوّق.
    //   3. ما زال فارغاً ⇒ اقتراح فئة يطابق اسمها الاستعلام (أو تصحيحه): بابٌ بدل نهاية مسدودة.
    //
    // مسار 2 و3 لا يعملان إلا على استعلام معقول (RecoveryMaxTokens كلمات، كل كلمة ≥ RecoveryMinTokenLength حرفاً).
    // ليس تجميلاً: نقطة البحث عامّة وبلا حدّ معدّل، ومسار التصحيح يقرأ مفردات المتجر ويحسب مسافات — فحدُّ المدخل
    // هو ما يمنع استعلاماً عبثياً متكرّراً من تضخيم الكلفة. وتصحيح كلمة من حرفين بلا معنى أصلاً.
    // ============================================================================
    public async Task<ProductSearchPage> SearchProductsAsync(
        ProductSearch search, PageRequest page, string culture, CancellationToken ct)
    {
        var tokens = SearchText.Tokenize(search.Keyword, SearchText.MaxQueryTokens);
        var expansions = await ExpandAsync(tokens, ct);
        var found = await MatchAsync(search, tokens, expansions, page, culture, ct);
        if (tokens.Count == 0 || found.TotalCount > 0) return new ProductSearchPage(found);

        var corrected = !search.ExactOnly && WorthRecovering(tokens) ? await CorrectAsync(tokens, ct) : null;
        var term = search.Keyword!.Trim();

        if (corrected is not null)
        {
            // الكلمة المصحَّحة تُوسَّع بمرادفاتها هي، لا بمرادفات الكلمة الخاطئة.
            var retry = await MatchAsync(search, corrected, await ExpandAsync(corrected, ct), page, culture, ct);
            if (retry.TotalCount > 0)
                return new ProductSearchPage(retry, new SearchRecovery(term, string.Join(' ', corrected), null));
        }

        var category = await SuggestCategoryAsync(corrected ?? tokens, culture, ct);
        return new ProductSearchPage(found, category is null ? null : new SearchRecovery(term, null, category));
    }

    // كلمة أو كلمتان أو ثلاث، كلٌّ ≥ 3 أحرف: نطاق الخطأ المطبعي الحقيقي، وحدُّ كلفة مسار الاسترجاع معاً.
    private static bool WorthRecovering(IReadOnlyList<string> tokens) =>
        tokens.Count <= RecoveryMaxTokens && tokens.All(t => t.Length >= RecoveryMinTokenLength);

    private async Task<PaginatedList<ProductDto>> MatchAsync(
        ProductSearch search, IReadOnlyList<string> tokens, IReadOnlyList<IReadOnlyList<string>> expansions,
        PageRequest page, string culture, CancellationToken ct)
    {
        var query = VisibleProducts();

        // ============================================================================
        // مطابقة كلمة البحث (M3، ADR-0042) — على الصورة المطبَّعة لا على النص الخام:
        //   • كل كلمة من كلمات الاستعلام شرط مستقلّ (AND): "مكنسة كهربائية" تُطابق اسماً يحمل الكلمتين ولو
        //     متفرّقتين، بعكس ما كان (سلسلة واحدة متّصلة يجب أن ترد حرفياً).
        //   • كل كلمة تُطابَق في اسم المنتج أو وصفه أو **اسم فئته** — والفئة إضافة M3: من يكتب اسم فئة يريد ما فيها.
        //   • بكل اللغات كما كان: متجر ثنائي اللغة يجد منتجه باسمه الإنجليزي ولو كان المتسوّق عربياً.
        //
        // `Contains` مع مُعامِل يُترجم إلى `LIKE N'%…%' ESCAPE N'\'` ويهرّب % و_ و[ من تلقائه — لا حقن LIKE.
        // (التعليق السابق هنا قال CHARINDEX؛ تحقّقنا في M3 بـ ToQueryString أنّه LIKE، وصُحّح في Catalog/README.md
        // حيث كانت الدعوى مسجَّلة صريحاً أنّها غير متحقَّق منها.)
        // ============================================================================
        var phrase = string.Join(' ', tokens);
        // كل كلمة تُوسَّع بمرادفاتها التي علّمها التاجر (M3): الشرط يصير "الكلمة أو أحد مرادفيها" — والشروط
        // بين الكلمات تبقى AND. يُطبَّق على المسار الساخن لا في الاسترجاع وحده: من كتب "جوال" في متجر يبيع
        // "هاتف" يجب أن يجد الهواتف **مع** أي منتج يحمل كلمة "جوال"، لا أن يُحرَم منها لأنّ شيئاً وُجد.
        query = expansions.Aggregate(query, (current, group) => current.Where(MatchesAny(group)));

        if (search.CategoryIds is { Count: > 0 } categoryIds)
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        // نطاق السعر: متغيّر واحد يقع داخل النطاق كاملاً (لا حدٌّ من متغيّر وحدٌّ من آخر). المطابقة على ما يمكن شراؤه؛
        // ومنتج لا يمكن شراء شيء منه يُطابَق بمتغيّراته النشطة كي يبقى في نطاق سعره وهو نافد.
        if (search.MinPrice is not null || search.MaxPrice is not null)
        {
            decimal? min = search.MinPrice, max = search.MaxPrice;
            query = query.Where(p =>
                p.Variants.Any(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0) && (min == null || v.Price.Amount >= min) && (max == null || v.Price.Amount <= max))
                || (!p.Variants.Any(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0))
                    && p.Variants.Any(v => v.IsActive && (min == null || v.Price.Amount >= min) && (max == null || v.Price.Amount <= max))));
        }
        if (search.OnSaleOnly)
            query = query.Where(p =>
                p.Variants.Any(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0) && EF.Property<decimal?>(v, "_compareAtAmount") > v.Price.Amount)
                || (!p.Variants.Any(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0))
                    && p.Variants.Any(v => v.IsActive && EF.Property<decimal?>(v, "_compareAtAmount") > v.Price.Amount)));

        return (await Sort(query, search.SortBy, phrase).ToPageAsync(Row(culture), page, ct)).Map(r => ToDto(r, culture));
    }

    // ============================================================================
    // الاقتراحات (M3، ADR-0042): البادئة أولاً ثم الاحتواء، منتجات ثم فئات.
    //
    // الترتيب هنا هو نفسه ترتيب المطابقة في البحث (RelevanceExpr) كي لا يعطي الاقتراح ترتيباً ثم تعطي صفحة
    // النتائج ترتيباً آخر لنفس الكلمة — تناقضٌ يراه المتسوّق ولا يفهمه.
    //
    // الفئات تُقترح بعد المنتجات دائماً وبعدد محدود: الفئة وجهة أوسع، ومن كتب كلمة يريد شيئاً بعينه أولاً.
    // ============================================================================
    public async Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(
        string? keyword, int limit, string culture, CancellationToken ct)
    {
        var tokens = SearchText.Tokenize(keyword, SearchText.MaxQueryTokens);
        var phrase = string.Join(' ', tokens);
        if (phrase.Length < SearchSuggestionRules.MinKeywordLength) return [];

        var take = Math.Clamp(limit, 1, SearchSuggestionRules.MaxLimit);
        // الفئات تأخذ ثلث المساحة على الأكثر، وواحدة على الأقلّ إن وُجدت — فلا تزحم قائمةً قصيرة.
        var categoryTake = Math.Max(1, take / 3);

        var products = await VisibleProducts()
            .Where(p => p.Translations.Any(t => t.NameNormalized.Contains(phrase)))
            .OrderByDescending(RelevanceExpr(phrase)).ThenByDescending(p => p.Id)
            .Take(take)
            .Select(p => new SuggestionRow(p.Id, p.Slug,
                p.Translations.Select(t => new NameRow(t.Culture, t.Name)).ToList(),
                p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).FirstOrDefault()))
            .ToListAsync(ct);

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.Translations.Any(t => t.NameNormalized.Contains(phrase)))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Take(categoryTake)
            .Select(c => new SuggestionRow(c.Id, c.Slug,
                c.Translations.Select(t => new NameRow(t.Culture, t.Name)).ToList(), null))
            .ToListAsync(ct);

        return products.Select(r => Suggestion("product", r, culture))
            .Concat(categories.Select(r => Suggestion("category", r, culture)))
            .Take(take)
            .ToList();
    }

    private static SearchSuggestionDto Suggestion(string kind, SuggestionRow row, string culture)
    {
        var names = Names(row.Names);
        return new SearchSuggestionDto(kind, row.Id, row.Slug,
            names.TryGetValue(culture, out var name) ? name : names.Values.FirstOrDefault() ?? row.Slug,
            row.ImageUrl);
    }

    public async Task<ProductDto?> FindActiveProductAsync(int id, string culture, CancellationToken ct) =>
        await DetailAsync(VisibleProducts().Where(p => p.Id == id), culture, ct);

    public async Task<ProductDto?> FindActiveProductBySlugAsync(string slug, string culture, CancellationToken ct) =>
        await DetailAsync(VisibleProducts().Where(p => p.Slug == slug), culture, ct);

    // نفس الفئة أولاً (الأكثر مبيعاً)، ثم أحدث منتجات الفئات الأخرى إن لم تكفِ — استعلامان صغيران محدودان بـ count.
    public async Task<IReadOnlyList<ProductDto>?> FindRelatedProductsAsync(int productId, int count, string culture, CancellationToken ct)
    {
        var categoryId = await VisibleProducts().Where(p => p.Id == productId)
            .Select(p => (int?)p.CategoryId).FirstOrDefaultAsync(ct);
        if (categoryId is null) return null;

        var sameCategory = await BestSellingFirst(VisibleProducts()
                .Where(p => p.CategoryId == categoryId && p.Id != productId))
            .Take(count).Select(Row(culture)).ToListAsync(ct);
        if (sameCategory.Count >= count) return sameCategory.Select(r => ToDto(r, culture)).ToList();

        var excluded = sameCategory.Select(p => p.Id).Append(productId).ToList();
        var others = await VisibleProducts()
            .Where(p => p.CategoryId != categoryId && !excluded.Contains(p.Id))
            .OrderByDescending(p => p.Id)
            .Take(count - sameCategory.Count).Select(Row(culture)).ToListAsync(ct);

        return sameCategory.Concat(others).Select(r => ToDto(r, culture)).ToList();
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(bool includeInactive, string culture, CancellationToken ct)
    {
        var categories = _db.Categories.AsNoTracking();
        if (!includeInactive) categories = categories.Where(c => c.IsActive);

        var rows = await categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new CategoryRow(c.Id, c.Slug, c.ParentId, c.SortOrder, c.IsActive,
                c.Translations.Select(t => new TextRow(t.Culture, t.Name, t.Description, t.MetaTitle, t.MetaDescription)).ToList()))
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var texts = Texts(r.Texts);
            return new CategoryDto(r.Id, r.Slug, Pick(texts, culture)?.Name ?? r.Slug, texts, r.ParentId, r.SortOrder, r.IsActive);
        }).ToList();
    }

    public async Task<PaginatedList<AdminProductListItemDto>> ListAdminProductsAsync(
        AdminProductSearch search, PageRequest page, string culture, CancellationToken ct)
    {
        var query = _db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            var sku = keyword.ToUpperInvariant();
            // الاسم يُطابَق على صورته المطبَّعة كما في بحث المتجر (M3): التاجر الذي يكتب "مكنسه" يجد "مَكْنَسَة"
            // التي كتبها هو. أمّا المعرّف (slug) وSKU فمُعرّفان تقنيان لاتينيان، يُطابَقان كما هما.
            var normalized = SearchText.Normalize(keyword);
            query = query.Where(p => p.Slug.Contains(keyword)
                                     || p.Translations.Any(t => t.NameNormalized.Contains(normalized))
                                     || p.Variants.Any(v => v.Sku != null && v.Sku.Contains(sku)));
        }
        if (search.Status is { } status) query = query.Where(p => p.Status == status);
        if (search.CategoryId is int categoryId) query = query.Where(p => p.CategoryId == categoryId);

        IOrderedQueryable<Product> ordered = search.SortBy switch
        {
            AdminProductSortBy.NameAsc => query.OrderBy(NameExpr(culture)).ThenByDescending(p => p.Id),
            AdminProductSortBy.PriceAsc => query.OrderBy(PriceExpr).ThenByDescending(p => p.Id),
            AdminProductSortBy.PriceDesc => query.OrderByDescending(PriceExpr).ThenByDescending(p => p.Id),
            AdminProductSortBy.StockAsc => query
                .OrderBy(p => _db.InventoryItems.Where(s => s.ProductId == p.Id).Sum(s => (int?)(s.OnHand - s.Reserved)) ?? 0)
                .ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        };

        return await ordered.ToPageAsync(p => new AdminProductListItemDto(
            p.Id, p.Slug,
            p.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                ?? p.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault() ?? p.Slug,
            p.Status.ToString(),
            p.Variants.Where(v => v.IsDefault).Select(v => v.Sku).FirstOrDefault(),
            p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Amount).FirstOrDefault(),
            p.Variants.Where(v => v.IsDefault).Select(v => EF.Property<decimal?>(v, "_compareAtAmount")).FirstOrDefault(),
            p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Currency).FirstOrDefault() ?? "",
            _db.InventoryItems.Where(s => s.ProductId == p.Id).Sum(s => (int?)(s.OnHand - s.Reserved)) ?? 0,
            _db.InventoryItems.Where(s => s.ProductId == p.Id).Select(s => (int?)s.LowStockThreshold).Min() ?? 0,
            p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).FirstOrDefault(),
            p.CategoryId,
            p.Category!.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                ?? p.Category.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
            p.CreatedAt,
            p.Variants.Count()), page, ct);
    }

    // نموذج التعديل: التجمّع كاملاً (صف واحد) — استعلامات منفصلة للأبناء بدل ضرب الصفوف.
    public async Task<AdminProductDto?> FindAdminProductAsync(int id, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking()
            .Include(p => p.Translations).Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
            .Include(p => p.Options).ThenInclude(o => o.Translations)
            .Include(p => p.Options).ThenInclude(o => o.Values).ThenInclude(v => v.Translations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return null;

        // المخزون للعرض فقط (يُعدَّل بتصحيحات في وحدة Inventory): صفّ لكل متغيّر.
        var stock = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.ProductId == id)
            .Select(i => new { i.VariantId, i.OnHand, i.Reserved, i.LowStockThreshold })
            .ToDictionaryAsync(i => i.VariantId, ct);

        var options = product.Options.OrderBy(o => o.Position).ThenBy(o => o.Id).ToList();
        var valuePosition = options.SelectMany((o, index) => o.Values.Select(v => (v.Id, Rank: index * 100 + v.Position)))
            .ToDictionary(v => v.Id, v => v.Rank);

        // ترتيب المتغيّرات بقيمها وفق ترتيب الخيارات (S قبل M، ثم اللون) — مشتقّ لا مخزَّن.
        var variants = product.Variants
            .Select(v => (Variant: v, ValueIds: v.OptionValues.Select(ov => ov.OptionValueId)
                .OrderBy(valueId => valuePosition.GetValueOrDefault(valueId)).ToList()))
            .OrderBy(v => string.Join(',', v.ValueIds.Select(valueId => valuePosition.GetValueOrDefault(valueId).ToString("D5"))), StringComparer.Ordinal)
            .ThenBy(v => v.Variant.Id)
            .Select(v =>
            {
                var level = stock.GetValueOrDefault(v.Variant.Id);
                int onHandV = level?.OnHand ?? 0, reservedV = level?.Reserved ?? 0;
                return new AdminProductVariantDto(v.Variant.Id, v.Variant.IsDefault, v.Variant.IsActive, v.Variant.Sku,
                    v.Variant.Price.Amount, v.Variant.CompareAtPrice?.Amount, v.ValueIds,
                    onHandV, reservedV, onHandV - reservedV, level?.LowStockThreshold ?? 0);
            })
            .ToList();

        int onHand = variants.Sum(v => v.OnHand), reserved = variants.Sum(v => v.Reserved);
        var defaultVariant = product.DefaultVariant;

        return new AdminProductDto(
            product.Id, product.Slug, product.Status.ToString(),
            product.Translations.ToDictionary(t => t.Culture, t => new CatalogTextDto(t.Name, t.Description, t.MetaTitle, t.MetaDescription)),
            product.Sku, product.Price.Amount, product.CompareAtPrice?.Amount, product.Price.Currency,
            onHand, reserved, onHand - reserved, stock.GetValueOrDefault(defaultVariant.Id)?.LowStockThreshold ?? 0,
            product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => new ProductImageDto(i.Id, i.Url, i.SortOrder)).ToList(),
            product.VideoUrl, product.CategoryId, product.Brand, product.CreatedAt, product.UpdatedAt,
            options.Select(o => new AdminProductOptionDto(o.Id, o.Position, Names(o.Translations),
                o.Values.OrderBy(v => v.Position).ThenBy(v => v.Id)
                    .Select(v => new AdminProductOptionValueDto(v.Id, v.Position, Names(v.Translations))).ToList())).ToList(),
            variants, ProductVariantLimitsDto.Current);
    }

    private static IReadOnlyDictionary<string, string> Names(IEnumerable<OptionTranslation> translations) =>
        translations.OrderBy(t => t.Culture, StringComparer.Ordinal).ToDictionary(t => t.Culture, t => t.Name);

    // مفردات المتجر لشاشة التاجر: مرتَّبة كما يقرؤها إنسان (لغة، ثم الكلمة، ثم مرادفها) لا بترتيب الإدراج.
    public async Task<IReadOnlyList<SearchSynonymDto>> ListSearchSynonymsAsync(CancellationToken ct) =>
        await _db.SearchSynonyms.AsNoTracking()
            .OrderBy(s => s.Culture).ThenBy(s => s.TermNormalized).ThenBy(s => s.ExpansionNormalized)
            .Select(s => new SearchSynonymDto(
                s.Id, s.Culture, s.Term, s.TermNormalized, s.Expansion, s.ExpansionNormalized))
            .ToListAsync(ct);

    // ============================================================================
    // أثر البحث مُجمَّعاً بالكلمة (M13). الجدول الأكبر في النظام، فكل ما يلي مقصودٌ لئلّا يُقرأ منه صفٌّ زائد:
    //
    //   • **العدّ والتجميع في SQL**: `GROUP BY` على بادئة الفهرس (المستأجر، اللغة، الصورة المطبَّعة) وعددُ
    //     النتائج عمودٌ مُضمَّن فيه — فـ"كم بحثاً" و"كم منها بلا نتيجة" يُحسبان من الفهرس بلا لمس صفّ.
    //   • **الملخّص باستعلامٍ ثانٍ لا بجمع الصفحة**: صفحةُ عشرين كلمةً لا تعرف مجموع النافذة، وجمعُها كان
    //     سيُنتج رقماً يتغيّر بتغيّر الصفحة — وهو بالضبط الخطأ الذي أصلحه M12 في لوحة التقارير.
    //   • **وترتيبٌ حتميّ**: الأكثر بحثاً، ثم الأحدث، ثم الكلمة أبجدياً. بلا الفاصل الأخير تتبادل كلمتان
    //     متساويتان موضعيهما بين صفحةٍ وأخرى فتُعرض إحداهما مرّتين وتغيب الثانية.
    // ============================================================================
    public async Task<SearchInsightsPage> ListSearchInsightsAsync(
        SearchInsightFilter filter, PageRequest page, CancellationToken ct)
    {
        var rows = _db.SearchQueryLogs.AsNoTracking().Where(l => l.SearchedAt >= filter.Since);
        if (!string.IsNullOrWhiteSpace(filter.Culture))
            rows = rows.Where(l => l.Culture == filter.Culture);

        var grouped = rows
            .GroupBy(l => new { l.Culture, l.TermNormalized })
            .Select(g => new
            {
                g.Key.Culture,
                g.Key.TermNormalized,
                Searches = g.Count(),
                ZeroResultSearches = g.Count(l => l.ResultCount == 0),
                LastSearchedAt = g.Max(l => l.SearchedAt),
            });

        // ============================================================================
        // الملخّص كلّه من تجميعٍ **فوق** التجميع، لا من ثلاثة استعلامات:
        //   • مجموع البحوث   = SUM على الصفوف المُجمَّعة
        //   • الكلمات المختلفة = COUNT على الصفوف المُجمَّعة — لا `COUNT(DISTINCT ...)` على عمودين، فتلك
        //     ليست SQL صحيحة أصلاً (رفضها المُحوِّل: "DISTINCT * بلا تعيين نوع") والتجميع القائم يُغني عنها.
        //   • وهو على النافذة كما هي، **قبل** مرشّح "بلا نتيجة": لو طُبِّق المرشّح أولاً لصار المجموع مساوياً
        //     للصفر دائماً، فقرأ التاجر أنّ كل بحوثه تفشل.
        // ============================================================================
        var summary = await grouped
            .GroupBy(_ => 1)
            .Select(g => new SearchInsightsSummary(
                g.Sum(x => x.Searches), g.Count(), g.Sum(x => x.ZeroResultSearches)))
            .FirstOrDefaultAsync(ct) ?? SearchInsightsSummary.Empty;

        // "لم تجد شيئاً ولا مرّة" — المقارنة بين مُجمَّعين، فهي في HAVING لا في WHERE.
        var listed = grouped;
        if (filter.OnlyZeroResults)
            listed = grouped.Where(g => g.ZeroResultSearches == g.Searches);

        // بلا مرشّحٍ فعددُ الصفحات هو عدد الكلمات المختلفة الذي حُسب أعلاه — فلا استعلام عدٍّ ثانٍ.
        var total = filter.OnlyZeroResults ? await listed.CountAsync(ct) : summary.DistinctTerms;
        var slice = await listed
            .OrderByDescending(g => g.Searches)
            .ThenByDescending(g => g.LastSearchedAt)
            .ThenBy(g => g.TermNormalized)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(ct);

        // ما كُتب فعلاً لكلمات هذه الصفحة وحدها: استعلامٌ ثانٍ محدودٌ بعشرين مفتاحاً، لا نافذةٌ كاملة تُقرأ
        // إلى الذاكرة. ويُطلب أحدثُ صورةٍ كُتبت لأنّها أقرب إلى ما يكتبه الزبائن الآن.
        var keys = slice.Select(g => g.TermNormalized).Distinct().ToList();
        var typed = (await rows
                .Where(l => keys.Contains(l.TermNormalized))
                .GroupBy(l => new { l.Culture, l.TermNormalized })
                .Select(g => new
                {
                    g.Key.Culture,
                    g.Key.TermNormalized,
                    Term = g.OrderByDescending(l => l.SearchedAt).ThenByDescending(l => l.Id)
                        .Select(l => l.Term).First(),
                })
                .ToListAsync(ct))
            .ToDictionary(t => (t.Culture, t.TermNormalized), t => t.Term);

        var items = slice
            .Select(g => new SearchTermInsightDto(
                g.Culture,
                // الصورة المطبَّعة بديلاً لا يُتوقَّع بلوغه: الصفّ الذي جاء منه المفتاح موجودٌ بالتعريف.
                typed.GetValueOrDefault((g.Culture, g.TermNormalized), g.TermNormalized),
                g.TermNormalized,
                g.Searches,
                g.ZeroResultSearches,
                g.LastSearchedAt))
            .ToList();

        return new SearchInsightsPage(
            new PaginatedList<SearchTermInsightDto>(items, total, page.Page, page.PageSize), summary);
    }

    // ── استرجاع الخطأ المطبعي (M3) ──────────────────────────────────────────

    // حدود مسار الاسترجاع. أرقام مقيسة لا مختارة: انظر "Measured evidence" في ADR-0042.
    private const int RecoveryMaxTokens = 3;
    private const int RecoveryMinTokenLength = 3;

    // سقف أسماء المفردات المقروءة. مفردات لغة طبيعية تتشبّع: متجر بخمسين ألف منتج لا يحمل خمسين ألف كلمة مختلفة.
    // السقف يمنع استعلاماً واحداً من قراءة كتالوج ضخم كاملاً، ولا يُفقد استرجاعاً إلا في كتالوج أكبر من أي مقيس.
    private const int VocabularyNameLimit = 5_000;

    // ============================================================================
    // توسيع كلمات الاستعلام بمفردات المتجر (M3، ADR-0042): لكل كلمة قائمةٌ تبدأ بها هي ثم مرادفاتها.
    //
    // استعلام واحد مفهرس لكل بحث ((المستأجر، اللغة، الكلمة المطبَّعة) مع المرادف عموداً مُضمَّناً)، ويعود فارغاً
    // في متجر لا مفردات له — وهو الحال الغالب. الكلفة المقيسة لبحثٍ في فهرس كهذا 0.2ms (ADR-0042)، وهي الثمن
    // المقبول لأن يعمل المرادف على المسار الساخن بدل أن يكون خطّة احتياطية لا تعمل إلا حين لا نتائج.
    //
    // **بلا تعدٍّ**: مرادف المرادف لا يُطبَّق (انظر SearchSynonym) — فحلقةٌ في البيانات لا تُنتج استعلاماً
    // لا ينتهي، وأثر كل صفّ مرئي لمن أضافه.
    //
    // اللغة لا تُصفّى عند المطابقة: البحث يطابق ترجمات كل اللغات أصلاً، والكلمات نفسها خاصّة بلغتها فعلياً.
    // عمود Culture تسميةٌ للتاجر في شاشته ونطاقٌ للفريد، لا مرشّح قراءة.
    // ============================================================================
    private async Task<IReadOnlyList<IReadOnlyList<string>>> ExpandAsync(
        IReadOnlyList<string> tokens, CancellationToken ct)
    {
        if (tokens.Count == 0) return [];

        var rows = await _db.SearchSynonyms.AsNoTracking()
            .Where(s => tokens.Contains(s.TermNormalized))
            .Select(s => new { s.TermNormalized, s.ExpansionNormalized })
            .ToListAsync(ct);

        if (rows.Count == 0) return tokens.Select(t => (IReadOnlyList<string>)[t]).ToList();

        return tokens.Select(token =>
        {
            // الكلمة نفسها أولاً دائماً، ثم مرادفاتها مرتَّبةً — فالنتيجة لا تتبع ترتيب صفوف القاعدة.
            var words = new List<string> { token };
            words.AddRange(rows.Where(r => r.TermNormalized == token)
                .Select(r => r.ExpansionNormalized)
                .Where(e => e != token)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(e => e, StringComparer.Ordinal));
            return (IReadOnlyList<string>)words;
        }).ToList();
    }

    // ============================================================================
    // "الكلمة أو أحد مرادفيها": شرط واحد بـ OR بين مطابقات الكلمات.
    //
    // تُبنى شجرة التعبير بيد لأنّ عدد المرادفات متغيّر، ولا يمكن التعبير عنه بـ LINQ ساكن: EF يترجم
    // `list.Contains(column)` إلى IN، لكن لا يترجم `list.Any(w => column.Contains(w))` — والمطلوب هو الثاني.
    // ولا نستعمل Expression.Invoke (لا يُترجَم): تُعاد كتابة مُعامِل كل تعبير إلى مُعامِل واحد مشترك بزائر،
    // وهي الطريقة القياسية لتركيب المُسنَدات في EF.
    // ============================================================================
    private static Expression<Func<Product, bool>> MatchesAny(IReadOnlyList<string> words)
    {
        var first = MatchesToken(words[0]);
        if (words.Count == 1) return first;

        var parameter = first.Parameters[0];
        var body = first.Body;
        for (var index = 1; index < words.Count; index++)
        {
            var next = MatchesToken(words[index]);
            var rebound = new ParameterRebinder(next.Parameters[0], parameter).Visit(next.Body)!;
            body = Expression.OrElse(body, rebound);
        }

        return Expression.Lambda<Func<Product, bool>>(body, parameter);
    }

    private sealed class ParameterRebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterRebinder(ParameterExpression from, ParameterExpression to) { _from = from; _to = to; }

        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : node;
    }

    // ============================================================================
    // تصحيح كل كلمة إلى أقرب كلمة في مفردات الكتالوج. الكلمة الموجودة أصلاً لا تُمسّ — وإلا صُحِّح ما هو صحيح.
    // يعود null إن لم تتغيّر كلمة واحدة، فلا إعادة بحث بلا داعٍ.
    // ============================================================================
    private async Task<IReadOnlyList<string>?> CorrectAsync(IReadOnlyList<string> tokens, CancellationToken ct)
    {
        var vocabulary = await VocabularyAsync(ct);
        if (vocabulary.Count == 0) return null;

        var corrected = new List<string>(tokens.Count);
        var changed = false;
        foreach (var token in tokens)
        {
            if (vocabulary.ContainsKey(token)) { corrected.Add(token); continue; }

            var closest = Closest(token, vocabulary);
            corrected.Add(closest ?? token);
            changed |= closest is not null;
        }

        return changed ? corrected : null;
    }

    // ============================================================================
    // مفردات المتجر: كلمات أسماء المنتجات المعروضة والفئات المفعَّلة، مع تكرار كل كلمة.
    // التكرار يُستعمل لكسر التعادل: كلمتان على المسافة نفسها ⇒ الأشيع أولى، لأنّها الأرجح أن تكون المقصودة.
    // استعلامان منفصلان لا Union: كلاهما إسقاط عمود واحد، وUnion على تنقّلات متداخلة لا يُترجَم موثوقاً.
    //
    // والترتيب قبل السقف ليس زينة: `Take` بلا `ORDER BY` يترك لـ SQL Server أن يعيد **أيّ** خمسة آلاف
    // اسم، وقد يعيد غيرها في التنفيذ التالي (خطّة مختلفة، توازٍ، ترتيب صفحات). في متجرٍ أسماؤه المختلفة
    // أكثر من السقف يعني ذلك مفرداتٍ تتبدّل بين طلبٍ وطلب: الخطأ المطبعي نفسه يُسترجَع مرّةً ولا يُسترجَع
    // أخرى، بلا أن يتغيّر الكتالوج. وهي القاعدة التي يفرضها `QueryableExtensions.ToPageAsync` بالتوقيع
    // (IOrderedQueryable) — وهذا الموضع الوحيد الذي لا يمرّ به فلا يفرضها عليه نوعٌ، فتُكتب صراحةً.
    // المطلوب ثباتُ الاختيار لا تفضيلُ حرفٍ على حرف؛ ترتيب الاسم أبسط ثابتٍ متاح. (نبّه عليه EF نفسه:
    // "row limiting operator without an OrderBy … may lead to unpredictable results".)
    // ============================================================================
    private async Task<Dictionary<string, int>> VocabularyAsync(CancellationToken ct)
    {
        var productNames = await VisibleProducts()
            .SelectMany(p => p.Translations.Select(t => t.NameNormalized))
            .Where(name => name != "")
            .Distinct().OrderBy(name => name).Take(VocabularyNameLimit).ToListAsync(ct);

        var categoryNames = await _db.Categories.AsNoTracking().Where(c => c.IsActive)
            .SelectMany(c => c.Translations.Select(t => t.NameNormalized))
            .Where(name => name != "")
            .Distinct().OrderBy(name => name).Take(VocabularyNameLimit).ToListAsync(ct);

        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in productNames.Concat(categoryNames))
            foreach (var word in SearchText.Tokenize(name))
                vocabulary[word] = vocabulary.GetValueOrDefault(word) + 1;

        return vocabulary;
    }

    // ============================================================================
    // أقرب كلمة، **حتمياً**: أقلّ مسافة، ثم الأشيع، ثم الأسبق ترتيباً حرفياً. الكسر الثلاثي مقصود — بلا الكسر
    // الأخير لكانت النتيجة تابعة لترتيب تعداد القاموس، أي إجابتين مختلفتين للاستعلام نفسه. ولا خروج مبكّر
    // عند مسافة 1 للسبب عينه.
    // ============================================================================
    private static string? Closest(string token, Dictionary<string, int> vocabulary)
    {
        string? best = null;
        int bestDistance = int.MaxValue, bestCount = 0;

        foreach (var (word, count) in vocabulary)
        {
            var distance = SearchDistance.Between(word, token);
            if (distance == SearchDistance.Beyond) continue;

            var better = distance < bestDistance
                         || (distance == bestDistance && count > bestCount)
                         || (distance == bestDistance && count == bestCount && string.CompareOrdinal(word, best) < 0);
            if (!better) continue;

            best = word; bestDistance = distance; bestCount = count;
        }

        return best;
    }

    // فئة مفعَّلة يطابق اسمها المطبَّع أول كلمة — الأصغر ترتيباً ثم بالمعرّف، كترتيب قائمة الفئات نفسها.
    private async Task<SearchCategorySuggestion?> SuggestCategoryAsync(
        IReadOnlyList<string> tokens, string culture, CancellationToken ct)
    {
        if (tokens.Count == 0) return null;
        var word = tokens[0];

        var row = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.Translations.Any(t => t.NameNormalized.Contains(word)))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new CategoryNameRow(c.Id, c.Slug,
                c.Translations.Select(t => new NameRow(t.Culture, t.Name)).ToList()))
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var names = Names(row.Names);
        return new SearchCategorySuggestion(row.Id, row.Slug,
            names.TryGetValue(culture, out var name) ? name : names.Values.FirstOrDefault() ?? row.Slug);
    }

    // ── داخلي ───────────────────────────────────────────────────────────────

    // المعروض: نشط وفي فئة مفعّلة. بوّابة V2 المؤقّتة (متغيّر نشط واحد) أُزيلت في V3 مع بناء اختيار المتغيّر: منتج
    // بعدّة متغيّرات نشطة يُعرض ويُشترى باختيار صريح، ومنتج نفد كل المتاح منه يبقى معروضاً غير متاح (قرار V3-b).
    private IQueryable<Product> VisibleProducts() =>
        _db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Category!.IsActive);

    private async Task<ProductDto?> DetailAsync(IQueryable<Product> product, string culture, CancellationToken ct)
    {
        var row = await product.Select(Row(culture)).FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var images = await _db.Set<ProductImage>().AsNoTracking()
            .Where(i => EF.Property<int>(i, "ProductId") == row.Id)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).ToListAsync(ct);
        var (options, variants) = await VariantModelAsync(row.Id, ct);
        return ToDto(row, culture, images, options, variants);
    }

    // ============================================================================
    // نموذج الاختيار للمتسوّق (V3): المتغيّرات النشطة بقيمها وأسعارها ومتاحها، والخيارات بقيمها المستخدمة في متغيّر نشط.
    //   • المعطّل مخفيّ تماماً (قرار V3-a): التاجر سحبه من البيع كما يُسحب منتج، فلا يُعرض ولا يُعطّل زرّاً.
    //   • النافد (متاح 0) يُعرض معطّلاً: المخزون مؤقّت (P-08c) — والواجهة تعرف ذلك من Available.
    //   • منتج بلا خيارات: لا شيء — عقده كما كان قبل V3 حرفياً (المتغيّر الضمني يحلّه الخادم عند الإضافة).
    // ============================================================================
    private async Task<(IReadOnlyList<ProductOptionDto>?, IReadOnlyList<ProductVariantDto>?)> VariantModelAsync(
        int productId, CancellationToken ct)
    {
        var options = await _db.Set<ProductOption>().AsNoTracking()
            .Where(o => EF.Property<int>(o, "ProductId") == productId)
            .OrderBy(o => o.Position).ThenBy(o => o.Id)
            .Select(o => new OptionRow(o.Id,
                o.Translations.Select(t => new NameRow(t.Culture, t.Name)).ToList(),
                o.Values.OrderBy(v => v.Position).ThenBy(v => v.Id)
                    .Select(v => new ValueRow(v.Id, v.Translations.Select(t => new NameRow(t.Culture, t.Name)).ToList())).ToList()))
            .ToListAsync(ct);
        if (options.Count == 0) return (null, null);

        var variants = await _db.Set<ProductVariant>().AsNoTracking()
            .Where(v => EF.Property<int>(v, "ProductId") == productId && v.IsActive)
            .Select(v => new VariantRow(v.Id, v.Price.Amount, EF.Property<decimal?>(v, "_compareAtAmount"),
                _db.InventoryItems.Where(i => i.VariantId == v.Id).Select(i => i.OnHand - i.Reserved).FirstOrDefault(),
                v.OptionValues.Select(ov => ov.OptionValueId).ToList()))
            .ToListAsync(ct);

        var used = variants.SelectMany(v => v.OptionValueIds).ToHashSet();
        return (
            options.Select(o => new ProductOptionDto(o.Id, Names(o.Names),
                    o.Values.Where(v => used.Contains(v.Id)).Select(v => new ProductOptionValueDto(v.Id, Names(v.Names))).ToList()))
                .Where(o => o.Values.Count > 0).ToList(),
            variants.Select(v => new ProductVariantDto(
                    v.Id, v.OptionValueIds, v.Price, v.CompareAtPrice, Math.Max(v.Available, 0)))
                .ToList());
    }

    private static IReadOnlyDictionary<string, string> Names(IEnumerable<NameRow> names) =>
        names.OrderBy(n => n.Culture, StringComparer.Ordinal).ToDictionary(n => n.Culture, n => n.Name);

    private static readonly Expression<Func<Product, decimal>> PriceExpr =
        p => p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Amount).FirstOrDefault();

    // السعر الذي يُرتَّب به المتجر = السعر المعروض: أرخص ما يمكن شراؤه، وإلا أرخص متغيّر نشط.
    private Expression<Func<Product, decimal>> StorefrontPriceExpr => p =>
        (p.Variants.Where(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0))
             .Select(v => (decimal?)v.Price.Amount).Min()
         ?? p.Variants.Where(v => v.IsActive).Select(v => (decimal?)v.Price.Amount).Min()) ?? 0m;

    private static Expression<Func<Product, string?>> NameExpr(string culture) =>
        p => p.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
             ?? p.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault();

    // المتاح للبيع من وحدة Inventory (المرحلة 6): الموجود − المحجوز، استعلام فرعي مترابط في SQL نفسه — للمتغيّرات النشطة
    // وحدها: مخزون متغيّر معطّل لا يُباع. والسعر من أرخص متغيّر قابل للشراء (Cheapest)، وإلا أرخص نشط (CheapestActive).
    private Expression<Func<Product, ProductRow>> Row(string culture) => p => new ProductRow(
        p.Id, p.Slug,
        p.Translations.Select(t => new TextRow(t.Culture, t.Name, t.Description, t.MetaTitle, t.MetaDescription)).ToList(),
        p.Variants.Where(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0))
            .OrderBy(v => v.Price.Amount).ThenBy(v => v.Id)
            .Select(v => new PriceRow(v.Price.Amount, EF.Property<decimal?>(v, "_compareAtAmount"), v.Price.Currency)).FirstOrDefault(),
        p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price.Amount).ThenBy(v => v.Id)
            .Select(v => new PriceRow(v.Price.Amount, EF.Property<decimal?>(v, "_compareAtAmount"), v.Price.Currency)).FirstOrDefault(),
        p.Variants.Where(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0))
            .Select(v => (decimal?)v.Price.Amount).Max(),
        p.Variants.Count(v => v.IsActive),
        _db.InventoryItems.Where(s => s.ProductId == p.Id && p.Variants.Any(v => v.Id == s.VariantId && v.IsActive))
            .Sum(s => (int?)(s.OnHand - s.Reserved)) ?? 0,
        p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).FirstOrDefault(),
        p.VideoUrl, p.CategoryId,
        p.Category!.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
            ?? p.Category.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
        p.Brand);

    private static ProductDto ToDto(
        ProductRow r, string culture, IReadOnlyList<string>? images = null,
        IReadOnlyList<ProductOptionDto>? options = null, IReadOnlyList<ProductVariantDto>? variants = null)
    {
        var texts = Texts(r.Texts);
        var main = Pick(texts, culture);
        // "ابتداءً من" حين يختلف أرخص ما يمكن شراؤه عن أغلاه؛ بسعر واحد لا معنى لها، وبلا متغيّر قابل للشراء لا تُعرض.
        var price = r.Cheapest ?? r.CheapestActive;
        var priceIsFrom = r.Cheapest is not null && r.HighestPurchasable > r.Cheapest.Price;
        return new ProductDto(
            r.Id, r.Slug, main?.Name ?? r.Slug, main?.Description, texts,
            price?.Price ?? 0m, price?.CompareAtPrice, price?.Currency ?? "", r.StockQuantity,
            r.ImageUrl, images, r.VideoUrl, r.CategoryId, r.CategoryName, r.Brand,
            priceIsFrom, r.ActiveVariants > 1, options, variants);
    }

    private static IReadOnlyDictionary<string, CatalogTextDto> Texts(IEnumerable<TextRow> rows) =>
        rows.ToDictionary(t => t.Culture, t => new CatalogTextDto(t.Name, t.Description, t.MetaTitle, t.MetaDescription));

    private static CatalogTextDto? Pick(IReadOnlyDictionary<string, CatalogTextDto> texts, string culture) =>
        texts.TryGetValue(culture, out var text)
            ? text
            : texts.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Value).FirstOrDefault();

    // كل ترتيب ينتهي بكاسر تعادل بالمعرّف: منتجان بنفس السعر لا يتبادلان موقعيهما بين طلبين.
    private IOrderedQueryable<Product> Sort(IQueryable<Product> query, ProductSortBy sortBy, string phrase) => sortBy switch
    {
        ProductSortBy.PriceAsc => query.OrderBy(StorefrontPriceExpr).ThenByDescending(p => p.Id),
        ProductSortBy.PriceDesc => query.OrderByDescending(StorefrontPriceExpr).ThenByDescending(p => p.Id),
        ProductSortBy.BestSelling => BestSellingFirst(query),
        // بلا كلمة بحث لا درجة مطابقة، فالترتيب الأحدث — لا ترتيب عشوائي ولا خطأ.
        ProductSortBy.Relevance when phrase.Length > 0 =>
            query.OrderByDescending(RelevanceExpr(phrase)).ThenByDescending(p => p.Id),
        _ => query.OrderByDescending(p => p.Id),
    };

    // ============================================================================
    // مطابقة كلمة واحدة من الاستعلام: الاسم أو الوصف أو اسم الفئة، بأي لغة، على الصورة المطبَّعة.
    // الوصف المطبَّع null حين لا وصف — فالشرط يفحص ذلك أولاً كي لا يُطابِق استعلامٌ وصفاً لا وجود له.
    // ============================================================================
    private static Expression<Func<Product, bool>> MatchesToken(string token) => p =>
        p.Translations.Any(t => t.NameNormalized.Contains(token)
                                || (t.DescriptionNormalized != null && t.DescriptionNormalized.Contains(token)))
        || p.Category!.Translations.Any(t => t.NameNormalized.Contains(token));

    // ============================================================================
    // درجة المطابقة، محسوبة في SQL (M3، ADR-0042) — لا في الواجهة: الترقيم يجب أن يرتّب كل الصفوف لا صفحةً منها،
    // فدرجة تُحسب بعد الجلب تُعطي صفحة ثانية لا تكمل الأولى.
    //
    // الدرجات: الاسم كاملاً = الاستعلام (100) ← أقوى إشارة ممكنة؛ فالاسم يبدأ بالاستعلام (90)؛ فيحتويه كعبارة
    // متّصلة (80)؛ فيحتوي كلمته الأولى (60)؛ فاسم الفئة يحتوي الكلمة الأولى (40)؛ وإلّا فالمطابقة جاءت من الوصف
    // أو من كلمات متفرّقة (20). الثلاثة الأولى بحث فهرس على IX_*_TenantId_NameNormalized؛ والباقي مسح فهرسٍ ضيّق.
    //
    // **تبسيط مقصود:** درجات "في الاسم" تقيس العبارة كاملةً والكلمة الأولى، لا كل كلمة على حدة — لأنّ درجةً
    // لكل كلمة تحتاج بناء شجرة تعبير ديناميكياً بعدد كلمات متغيّر، وهو تعقيد لا يشتريه تحسّن ترتيب ملموس:
    // شرط الـ AND في التصفية ضمن أصلاً أنّ كل الكلمات موجودة، والدرجة هنا ترتّب من بينهم. مسجَّل في
    // TechnicalDebt.md كمسار تحسين إن أظهرت بيانات M13 حاجةً مقيسة.
    // ============================================================================
    private static Expression<Func<Product, int>> RelevanceExpr(string phrase)
    {
        var firstToken = phrase.Split(' ')[0];
        return p =>
            p.Translations.Any(t => t.NameNormalized == phrase) ? 100
            : p.Translations.Any(t => t.NameNormalized.StartsWith(phrase)) ? 90
            : p.Translations.Any(t => t.NameNormalized.Contains(phrase)) ? 80
            : p.Translations.Any(t => t.NameNormalized.Contains(firstToken)) ? 60
            : p.Category!.Translations.Any(t => t.NameNormalized.Contains(firstToken)) ? 40
            : 20;
    }

    // "الأكثر مبيعاً" = مجموع الكميات عبر الطلبات المُسلَّمة فقط — استعلام فرعي مترابط واحد في SQL.
    private IOrderedQueryable<Product> BestSellingFirst(IQueryable<Product> query) =>
        query.OrderByDescending(p =>
                _db.Orders.Where(o => o.Status == OrderStatus.Delivered)
                    .SelectMany(o => o.Items)
                    .Where(i => i.ProductId == p.Id)
                    .Sum(i => (int?)i.Quantity) ?? 0)
            .ThenByDescending(p => p.Id);

    private sealed record TextRow(string Culture, string Name, string? Description, string? MetaTitle, string? MetaDescription);

    private sealed record ProductRow(
        int Id, string Slug, List<TextRow> Texts, PriceRow? Cheapest, PriceRow? CheapestActive, decimal? HighestPurchasable,
        int ActiveVariants, int StockQuantity, string? ImageUrl, string? VideoUrl, int CategoryId, string? CategoryName,
        string? Brand);

    // سعر متغيّر واحد بعملته وسعر مقارنته — الزوج معاً كي لا يُركَّب سعرُ متغيّر مع مقارنةِ آخر.
    private sealed record PriceRow(decimal Price, decimal? CompareAtPrice, string Currency);

    private sealed record NameRow(string Culture, string Name);
    private sealed record OptionRow(int Id, List<NameRow> Names, List<ValueRow> Values);
    private sealed record ValueRow(int Id, List<NameRow> Names);
    private sealed record VariantRow(int Id, decimal Price, decimal? CompareAtPrice, int Available, List<int> OptionValueIds);

    private sealed record CategoryRow(int Id, string Slug, int? ParentId, int SortOrder, bool IsActive, List<TextRow> Texts);

    private sealed record CategoryNameRow(int Id, string Slug, List<NameRow> Names);

    private sealed record SuggestionRow(int Id, string Slug, List<NameRow> Names, string? ImageUrl);
}
