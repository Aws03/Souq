using Souq.Domain.Common;

namespace Souq.Domain.Entities;

// ============================================================================
// Category — فئة المنتجات (مثل: إلكترونيات، ملابس).
// ParentId اختياري (?) ليدعم فئات متفرّعة (إلكترونيات > هواتف) إن احتجنا لاحقاً.
// لاحظ التصميم لاحتمال التوسّع دون تعقيد الحاضر (مبدأ: صمّم للتغيير).
// ============================================================================
public class Category : Entity, ITenantOwned
{
    public int TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;  // معرّف نصّي للرابط: /category/electronics
    public int? ParentId { get; private set; }

    // مُنشئ خاص فارغ لأجل EF Core فقط (يحتاجه ليُنشئ الكائن عند القراءة من DB).
    private Category() { }

    public Category(string name, string slug, int? parentId = null)
    {
        Name = name;
        Slug = slug;
        ParentId = parentId;
    }

    // تحديث بيانات الفئة (للمدير). تفرّد الـ slug وصحّة الأب يُحرَسان في المعالج
    // لأنهما يحتاجان استعلام قاعدة البيانات (لا يملك الكيان وصولاً إليها).
    public void UpdateDetails(string name, string slug, int? parentId = null)
    {
        Name = name;
        Slug = slug;
        ParentId = parentId;
    }
}
