namespace Souq.Domain.Enums;

// معيار ترتيب كتالوج المنتجات. يعيش في Domain لأن IProductRepository (في
// Domain) يستقبله ضمن توقيع SearchAsync — ولا يجوز لطبقة داخلية الإشارة لخارجية.
public enum ProductSortBy
{
    Newest = 0,       // الأحدث أولاً (الافتراضي — نفس الترتيب التاريخي للكتالوج)
    PriceAsc = 1,     // السعر تصاعدياً
    PriceDesc = 2,    // السعر تنازلياً
    BestSelling = 3,  // الأكثر مبيعاً: مجموع الكميات عبر الطلبات المُسلَّمة فقط
}
