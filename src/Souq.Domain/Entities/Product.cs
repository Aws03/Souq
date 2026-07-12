using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Product — المنتج. هذا "نموذج غنيّ" (Rich Model) لا مجرد حقول.
// لماذا الخصائص set خاصة (private)؟ حتى لا يستطيع أي كود خارجي تغيير المخزون
// عشوائياً (product.Stock = -5). أي تغيير يمرّ عبر دوال تحرس القواعد.
// هذا هو "التغليف" (Encapsulation) — أهم مبدأ في حماية صحّة البيانات.
// ============================================================================
public class Product : Entity
{
    public string NameAr { get; private set; } = default!;
    public string NameEn { get; private set; } = default!;
    // توافق خلفي: أي كود قديم يقرأ Name (رسائل استثناءات، لقطة اسم سطر الطلب)
    // يستمر بالعمل دون تعديل — يُقرأ من الاسم العربي دوماً.
    public string Name => NameAr;
    public string Description { get; private set; } = default!;
    public Money Price { get; private set; } = default!;   // كائن قيمة، لا decimal عارٍ
    public int StockQuantity { get; private set; }
    public string ImageUrl { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public int CategoryId { get; private set; }
    public Category? Category { get; private set; }          // علاقة تنقّل (Navigation)

    private Product() { }

    // nameEn اختياري: يُطابق الاسم العربي تلقائياً إن غاب (منتج لم يُترجم بعد
    // بدل قيمة فارغة في الواجهة). nameAr يبقى في موضعه الأصلي كي تستمر كل
    // استدعاءات المُنشئ القديمة (اختبارات/كود سابق) بالعمل دون تعديل.
    public Product(string nameAr, string description, Money price,
                   int stockQuantity, string imageUrl, int categoryId, string? nameEn = null)
    {
        NameAr = nameAr;
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn;
        Description = description;
        Price = price;
        StockQuantity = stockQuantity;
        ImageUrl = imageUrl;
        CategoryId = categoryId;
        IsActive = true;
    }

    // قاعدة عمل: هل يمكن طلب هذه الكمية؟ المنطق يعيش في المنتج نفسه،
    // لا متناثراً في كل مكان يستدعيه. مكان واحد للحقيقة.
    public bool CanFulfill(int quantity) => IsActive && quantity > 0 && quantity <= StockQuantity;

    // إنقاص المخزون عند البيع — محميّ بقاعدة. لا أحد يُنقص المخزون إلا عبر هذا الباب.
    public void DecreaseStock(int quantity)
    {
        if (!CanFulfill(quantity))
            throw new InsufficientStockException(Name, quantity, StockQuantity);
        StockQuantity -= quantity;
    }

    public void IncreaseStock(int quantity) => StockQuantity += quantity;

    // تعيين المخزون لقيمة مطلقة (تصحيح/إعادة تخزين من قبل الإدارة) — محروس بقاعدة:
    // لا يُسمح بقيمة سالبة. التعديل يمرّ عبر هذا الباب فقط، لا عبر set عام.
    public void SetStock(int quantity)
    {
        if (quantity < 0)
            throw new InvalidProductDataException("لا يمكن أن تكون كمية المخزون سالبة");
        StockQuantity = quantity;
    }

    // تحديث الحقول الوصفية للمنتج دفعة واحدة. السعر يبقى ضمن كائن قيمة Money
    // (يحرس قاعدة عدم السلبية). نُبقي IsActive/المخزون خارج هذه الدالة لأن لهما
    // أبواباً محروسة خاصة (Deactivate/SetStock) — كل قاعدة في موضعها الصحيح.
    public void UpdateDetails(string nameAr, string description, Money price,
                              string imageUrl, int categoryId, string? nameEn = null)
    {
        NameAr = nameAr;
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn;
        Description = description;
        Price = price;
        ImageUrl = imageUrl;
        CategoryId = categoryId;
    }

    // تعيين صورة المنتج بعد رفعها (باب محروس خاص، منفصل عن UpdateDetails لأن
    // الرفع عملية مستقلّة لها نقطتها الخاصة). لا يُقبل رابط فارغ.
    public void SetImageUrl(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new InvalidProductDataException("رابط الصورة مطلوب");
        ImageUrl = imageUrl;
    }

    // حذف منطقي بدل الفعلي (مبدأ من الملف: نحافظ على السجلات التاريخية).
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
