using Souq.Domain.Common;
using Souq.Domain.Interfaces;

namespace Souq.Domain.Entities;

// ============================================================================
// الربط بين زائرٍ مُعتِم وعميلٍ معروف — **جدولٌ منفصل من اليوم الأول** (ADR-0050 §5).
//
// مخزن الأحداث لا يحمل معرّف عميل أبداً؛ يحمل `VisitorId` وحده. وهذا الجدول هو الموضع **الوحيد**
// الذي يُوصَل فيه الاثنان، فيُجمَع سلوكُ ما قبل الدخول بما بعده من هنا ولا من أيّ مكان آخر.
//
// **والسبب كلّه في طلب المحو.** مخزن الأحداث يُكتب إضافةً فقط: إن كان معرّف العميل داخل صفوفه فمحوُ
// شخصٍ يعني إعادةَ كتابة مخزنٍ لا يُعاد كتابته — وهو نمط فشلٍ موثَّق لا مفاجأة. وبالفصل يصير المحو
// حذفَ صفوفٍ من جدول واحد: يفقد النظام **مَن** كان الزائر ويُبقي **ما حدث** بلا نسبة، وهو بالضبط
// ما يعنيه إخفاء الهوية.
//
// ولذلك أيضاً: القراءة منه محصورة في مسارٍ مراجَع، والتاجر لا يراه — لوحاته تقرأ المجاميع لا
// الروابط.
// ============================================================================
public class VisitorIdentityLink : Entity, ITenantOwned
{
    public int TenantId { get; private set; }

    public string VisitorId { get; private set; } = default!;
    public int CustomerId { get; private set; }

    // أوّل لحظةٍ عُرف فيها أنّ هذا الزائر هو هذا العميل. لا يُحدَّث: الربط واقعةٌ لها تاريخ.
    public DateTime LinkedAt { get; private set; }

    private VisitorIdentityLink() { }

    public static VisitorIdentityLink? For(string? visitorId, int customerId, DateTime utcNow)
    {
        var visitor = visitorId?.Trim();
        if (string.IsNullOrEmpty(visitor) || visitor.Length > BehaviouralEvent.IdentifierMaxLength) return null;
        if (customerId <= 0) return null;
        return new VisitorIdentityLink { VisitorId = visitor, CustomerId = customerId, LinkedAt = utcNow };
    }
}
