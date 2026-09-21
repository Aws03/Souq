using Souq.Domain.Common;
using Souq.Domain.Interfaces;

namespace Souq.Domain.Entities;

// ============================================================================
// تجميعةُ يومٍ لمنتج: ظهورٌ، نقرٌ، إضافةٌ للسلة، وشراء — **بلا أيّ معرّف شخص** (ADR-0050 §6).
//
// ولهذا تُحفَظ **بلا نهاية** بينما تُمسح الصفوف الخام: ما لا يُعرَّف شخصاً ليس بياناً شخصياً، فمدّة
// حفظه قرارٌ هندسيّ لا قانونيّ. فتقصيرُ مدّة الاحتفاظ بالخام يكلّف التفاصيل ولا يمحو التاريخ.
//
// **والترتيب ملزِم: التجميع قبل المسح.** المجموع لا يُحتسب من صفوفٍ مُسِحت، فوظيفةُ التجميع يجب أن
// تكون قد عملت على اليوم قبل أن يمسّه المسح — وهذا شرطٌ يفرضه الكود لا التوثيق (انظر مسار المسح).
// ============================================================================
public class ProductEngagementDaily : Entity, ITenantOwned
{
    public int TenantId { get; private set; }

    // اليومُ بتوقيت UTC (منتصف ليله). التاجر يقرأ يومَه هو في التقارير، والتحويل عند العرض —
    // لأنّ تجميعةً مبنيّةً على منطقةٍ زمنية تصير خطأً يوم يغيّر المتجر منطقته.
    public DateTime Day { get; private set; }
    public int ProductId { get; private set; }

    public int Impressions { get; private set; }
    public int Clicks { get; private set; }
    public int CartAdds { get; private set; }
    public int Purchases { get; private set; }
    public int UnitsSold { get; private set; }

    private ProductEngagementDaily() { }

    public static ProductEngagementDaily For(
        DateTime day, int productId, int impressions, int clicks, int cartAdds, int purchases, int unitsSold) => new()
    {
        Day = day.Date,
        ProductId = productId,
        Impressions = impressions,
        Clicks = clicks,
        CartAdds = cartAdds,
        Purchases = purchases,
        UnitsSold = unitsSold,
    };

    // التجميع يُعاد احتسابه لليوم كاملاً فيُستبدَل، لا يُجمَع فوق نفسه: إعادةُ تشغيلٍ لليوم نفسه
    // يجب أن تُنتج الرقم نفسه، وهو ما يجعل الوظيفة قابلةً لإعادة التشغيل بلا خوف.
    public void Replace(int impressions, int clicks, int cartAdds, int purchases, int unitsSold)
    {
        Impressions = impressions;
        Clicks = clicks;
        CartAdds = cartAdds;
        Purchases = purchases;
        UnitsSold = unitsSold;
    }
}

// ============================================================================
// تَجاوُرُ منتجَين في يوم: كم مرّة رُئيا في جلسةٍ واحدة، وكم مرّة اشتُريا في طلبٍ واحد.
//
// هذه هي التجميعة التي **لا يمكن احتسابها لاحقاً**: الجلسة والطلب هما ما يربط المنتجَين، وكلاهما
// يعيش في الصفوف الخام التي ستُمسح. ولذلك تُحتسب مع بقيّة التجميعات لا مع المرحلة التي ستستعملها
// (C10 موقوفة على C-09 — أيَجوز تجميع السلوك بين المتاجر؟ — وانتظارُها يكلّف التاريخ).
//
// الزوج مُرتَّب دائماً (`ProductIdLow < ProductIdHigh`) فلا يُخزَّن الزوج مرّتين، والعدد محدود
// بسقفٍ لكل جلسة وطلب: زوجٌ من كل اثنين في سلّةٍ فيها مئة سطر خمسة آلاف صفّ، والفائدة منها لا تُذكر.
// ============================================================================
public class ProductPairDaily : Entity, ITenantOwned
{
    // سقفُ العناصر التي تُولَّد منها الأزواج، لكل جلسة أو طلب. ما فوقه يُقصّ.
    public const int MaxItemsPerGroup = 20;

    public int TenantId { get; private set; }

    public DateTime Day { get; private set; }
    public int ProductIdLow { get; private set; }
    public int ProductIdHigh { get; private set; }

    public int CoViews { get; private set; }
    public int CoPurchases { get; private set; }

    private ProductPairDaily() { }

    public static ProductPairDaily? For(DateTime day, int first, int second, int coViews, int coPurchases)
    {
        if (first == second || first <= 0 || second <= 0) return null;
        return new ProductPairDaily
        {
            Day = day.Date,
            ProductIdLow = Math.Min(first, second),
            ProductIdHigh = Math.Max(first, second),
            CoViews = coViews,
            CoPurchases = coPurchases,
        };
    }

    public void Replace(int coViews, int coPurchases)
    {
        CoViews = coViews;
        CoPurchases = coPurchases;
    }
}
