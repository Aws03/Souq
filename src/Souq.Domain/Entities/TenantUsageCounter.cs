using Souq.Domain.Common;

namespace Souq.Domain.Entities;

// ============================================================================
// عدّاد استهلاك حدٍّ واحد في متجر واحد (C2، ADR-0049): صفٌّ لكل (متجر، اسم حدّ) يحمل المستهلَك.
// كيان متجر (ITenantOwned) لا جدول منصّة: مرشّح المستأجر هو ما يجعل جملة التحديث المجمَّعة في
// TenantQuotaGuard آمنة بلا شرط TenantId مكتوب بيد — كما في OrderNumberSequence تماماً.
//
// **لماذا عدّاد أصلاً، ولم يُعَدّ الشيء نفسه؟** لأن "عُدّ ثم اكتب" يفشل **مفتوحاً** تحت
// READ_COMMITTED_SNAPSHOT، وهو مُفعَّل افتراضياً على قواعد مُدارة يسمّيها توثيق هذا المستودع هدفاً
// ممكناً (TD-68). قفل التحديث على صفٍّ واحد لا يعتمد على مستوى العزل إطلاقاً. المفاضلة كاملة في
// ADR-0049.
//
// والفرق عن OrderNumberSequence — وهو مصدر الالتزام الوحيد الذي يزيده هذا الكيان: أرقام الطلبات
// تصعد فقط وتحتمل الفجوات صراحةً، والحدّ **ينزل** عند الحذف والأرشفة والمحو. فالانحراف ممكن، ولذلك
// يوجد المسح المصالِح (QuotaReconciliationJob): العدّاد ليس الحقيقة بل صورةٌ عنها تُصحَّح دورياً.
// ============================================================================
public class TenantUsageCounter : Entity, ITenantOwned
{
    private TenantUsageCounter() { }

    public int TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public int Used { get; private set; }

    // يُنشأ بعدد المستهلَك **الفعلي** لا بصفر: متجر قائم فيه خمسمئة منتج لا يبدأ عدّاده من الصفر
    // فيُهدى خمسمئة مجّاناً. من يُنشئه يَعُدّ أولاً (QuotaResources).
    public static TenantUsageCounter StartAt(string name, int used) =>
        new() { Name = name, Used = used >= 0 ? used : 0 };
}
