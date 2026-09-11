namespace Souq.Application.Common.Exceptions;

// ============================================================================
// أخطاء عزل المستأجرين — كلاهما خطأ برمجي لا مدخل مستخدم، فالـ API يترجمهما إلى 500 عام بلا
// تفاصيل. الفشل صاخب عمداً (MultiTenancy.md §4): استعلام بلا متجر لا يعيد "كل الصفوف" أبداً،
// وكتابة صف متجر آخر لا تُحفَظ أبداً.
// ============================================================================

// وصول لبيانات متجر بلا متجر محدَّد (مضيف المنصّة، مهمة خلفية نسيت ضبط السياق).
public sealed class TenantContextMissingException : Exception
{
    public TenantContextMissingException()
        : base("لا سياق متجر لهذه العملية: بيانات المتاجر لا تُقرأ ولا تُكتب بلا متجر محدَّد.") { }
}

// محاولة إضافة/تعديل/حذف صف يخصّ متجراً غير متجر السياق — رفضها حارس الكتابة قبل الحفظ.
public sealed class CrossTenantWriteException : Exception
{
    public CrossTenantWriteException(string entityType, int? entityTenantId, int? contextTenantId)
        : base($"رُفضت كتابة {entityType} للمتجر {entityTenantId?.ToString() ?? "المنصّة"} " +
               $"من سياق {contextTenantId?.ToString() ?? "المنصّة"}.")
    {
        EntityType = entityType;
        EntityTenantId = entityTenantId;
        ContextTenantId = contextTenantId;
    }

    public string EntityType { get; }
    public int? EntityTenantId { get; }
    public int? ContextTenantId { get; }
}
