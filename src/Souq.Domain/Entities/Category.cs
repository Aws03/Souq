using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Category — فئة المنتجات: شجرة داخل المتجر (المرحلة 5). نصوصها لكل لغة (D-10)، ترتيب عرض بين إخوتها، وعلَم
// تفعيل (فئة معطّلة تختفي من المتجر ومنتجاتها معها). النقل محروس: لا تصبح فئة أباً لنفسها ولا لأحد أسلافها
// (حلقة تكسر كل عرض شجري)، ولا تتجاوز الشجرة 5 مستويات. "من أسلاف الأب؟" سؤال قاعدة بيانات يجيبه المستدعي.
// ============================================================================
public class Category : Entity, ITenantOwned
{
    public const int SlugMaxLength = 100;
    public const int MaxDepth = 5;

    private readonly List<CategoryTranslation> _translations = new();

    public int TenantId { get; private set; }
    public string Slug { get; private set; } = default!;  // معرّف نصّي للرابط: /category/electronics
    public int? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<CategoryTranslation> Translations => _translations.AsReadOnly();
    public string Name => NameIn(null);

    // مُنشئ خاص فارغ لأجل EF Core فقط.
    private Category() { }

    public Category(string slug, IReadOnlyDictionary<string, CatalogText> texts, int sortOrder = 0)
    {
        SetSlug(slug);
        SetTexts(texts);
        SetSortOrder(sortOrder);
        IsActive = true;
    }

    public string NameIn(string? culture) => CatalogTranslation.NameIn(_translations, culture);

    public void SetTexts(IReadOnlyDictionary<string, CatalogText> texts) =>
        CatalogTranslation.Replace(_translations, CatalogText.NormalizeAll(texts, Invalid),
            (culture, text) => new CategoryTranslation(culture, text));

    public void SetSlug(string slug) =>
        Slug = CatalogSlug.TryNormalize(slug, SlugMaxLength)
               ?? throw new InvalidCategoryException("معرّف الرابط يقبل أحرفاً لاتينية صغيرة وأرقاماً وشرطات (2–100)");

    public void SetSortOrder(int sortOrder)
    {
        if (sortOrder < 0) throw new InvalidCategoryException("ترتيب العرض لا يكون سالباً");
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    // parentAncestry: الأب المقترح ثم أسلافه صعوداً حتى الجذر. subtreeHeight: عمق هذه الفئة مع فروعها (ورقة = 1).
    public void MoveTo(int? parentId, IReadOnlyCollection<int> parentAncestry, int subtreeHeight = 1)
    {
        if (parentId is null)
        {
            ParentId = null;
            return;
        }
        if (parentId == Id || parentAncestry.Contains(Id))
            throw new InvalidCategoryParentException("لا يمكن نقل الفئة تحت نفسها أو تحت إحدى فئاتها الفرعية");
        if (parentAncestry.Count + subtreeHeight > MaxDepth)
            throw new InvalidCategoryParentException($"شجرة الفئات حتى {MaxDepth} مستويات");
        ParentId = parentId;
    }

    private static Exception Invalid(string message) => new InvalidCategoryException(message);
}
