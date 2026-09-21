using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Domain.Entities;

// ============================================================================
// إعدادُ ضريبةِ متجرٍ واحد: أيَّ ملفَّ اختصاصٍ اختار، وهل فعَّل الجمع، وبأيّ رقم تسجيل
// ([ADR-0055](0055)).
//
// **وهذا هو موضعُ إعادة الاستخدام الذي طلبه قرار المالك بالنصّ**: متجرٌ ثانٍ في الاختصاص نفسه
// يختار الملفَّ الذي تحقّق منه محاسبُ الأوّل، ولا يُعيد إدخال شيء. فالقواعد تُحفَظ مرّةً في
// المنصّة، وما يملكه المتجر هو **الاختيار** لا القواعد.
//
// **والافتراضي: لا ملفّ ولا جمع.** متجرٌ قائم لا يتغيّر شيءٌ في حساباته يوم تُشحَن هذه القدرة —
// صفرُ الضريبة الصريح في خطّ التسعير يبقى كما هو، والفرقُ الوحيد أنّ له الآن مكاناً يُقال منه.
//
// **والمتجر لا يُدخل نسبةً.** ما يملكه: اختيارُ ملفّ، وتفعيلُ الجمع، ورقمُ تسجيله هو. النسبةُ
// قاعدةُ اختصاصٍ تحفظها المنصّة ويتحقّق منها مهنيّ — ولو كان كلُّ متجرٍ يُدخل نسبته لَما كان
// للتحقّق معنى، ولَعاد تشتُّتُ القيم الذي وُجد الملفّ ليُنهيه.
// ============================================================================
public class StoreTaxSettings : Entity, ITenantOwned
{
    public const int RegistrationNumberMaxLength = 60;

    public int TenantId { get; private set; }

    // الملفّ المختار. null ⇒ لم يختر المتجر اختصاصاً ⇒ لا ضريبة، والسببُ يُقال له.
    public int? TaxProfileId { get; private set; }

    // هل يجمع المتجر الضريبة فعلاً؟ الاختيارُ وحده لا يجمع: متجرٌ يُعِدّ نفسه ولم يُسجَّل بعد
    // يختار ملفَّه ويقرأ ملاحظاته قبل أن يمسّ سعراً واحداً.
    public bool CollectionEnabled { get; private set; }

    // رقمُ تسجيل المتجر الضريبي، إن كان له. يُعرَض على الفاتورة حيث يشترطه الاختصاص — ولا يفسّره
    // الكود ولا يتحقّق من شكله: أشكالُه تختلف باختلاف الاختصاص، وشكلٌ مفروضٌ من هنا يرفض رقماً صحيحاً.
    public string? RegistrationNumber { get; private set; }

    public DateTime? SelectedAt { get; private set; }

    private StoreTaxSettings() { }

    public static StoreTaxSettings None() => new();

    // اختيارُ ملفّ (أو إلغاء الاختيار بـ null). إلغاءُ الاختيار يُوقف الجمع معه: جمعٌ بلا ملفّ
    // لا معنى له، وتركُه مفعَّلاً يجعل الحالةَ تقول ما لا تفعله.
    public void SelectProfile(int? taxProfileId, DateTime utcNow)
    {
        TaxProfileId = taxProfileId;
        SelectedAt = taxProfileId is null ? null : utcNow;
        if (taxProfileId is null) CollectionEnabled = false;
    }

    public void SetCollection(bool enabled)
    {
        if (enabled && TaxProfileId is null)
            throw new InvalidStoreTaxSettingsException("اختر ملفّ ضريبةٍ قبل تفعيل الجمع");
        CollectionEnabled = enabled;
    }

    public void SetRegistrationNumber(string? number)
    {
        var trimmed = number?.Trim();
        if (trimmed is { Length: > RegistrationNumberMaxLength })
            throw new InvalidStoreTaxSettingsException($"رقم التسجيل الضريبي حتى {RegistrationNumberMaxLength} حرفاً");
        if (trimmed is not null && trimmed.Any(char.IsControl))
            throw new InvalidStoreTaxSettingsException("رقم التسجيل الضريبي يحتوي محارف غير مسموحة");
        RegistrationNumber = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
