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
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public Money Price { get; private set; } = default!;   // كائن قيمة، لا decimal عارٍ
    public int StockQuantity { get; private set; }
    public string ImageUrl { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public int CategoryId { get; private set; }
    public Category? Category { get; private set; }          // علاقة تنقّل (Navigation)

    private Product() { }

    public Product(string name, string description, Money price,
                   int stockQuantity, string imageUrl, int categoryId)
    {
        Name = name;
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
    public void UpdateDetails(string name, string description, Money price,
                              string imageUrl, int categoryId)
    {
        Name = name;
        Description = description;
        Price = price;
        ImageUrl = imageUrl;
        CategoryId = categoryId;
    }

    // حذف منطقي بدل الفعلي (مبدأ من الملف: نحافظ على السجلات التاريخية).
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
