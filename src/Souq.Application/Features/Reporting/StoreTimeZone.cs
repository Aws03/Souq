namespace Souq.Application.Features.Reporting;

// ============================================================================
// منطقة المتجر الزمنية، محلولةً إلى TimeZoneInfo (C11).
//
// **لماذا هذا الملف موجود أصلاً؟** لأن `Tenant.SetLocale` يتحقّق من **شكل** معرّف IANA ولا يسأل
// قاعدة مناطق نظام التشغيل — وتعليقه يقول ذلك صراحةً: "لا نعتمد على قاعدة مناطق نظام التشغيل داخل
// المجال". قرارٌ صحيح (المجال لا يعتمد على بيئة تشغيل)، ونتيجته أنّ معرّفاً سليم الشكل قد لا
// يُحلّ هنا: منطقة أُزيلت من قاعدة tzdb، أو نظام بلا ICU.
//
// فالتسامح في القراءة والتشدّد في الكتابة، كما في StoreModules.Parse وحدود الخطط: معرّف لا يُحلّ
// **يعود إلى UTC** ولا يُسقط اللوحة. والمقايضة صريحة: رقمٌ مُزاحٌ بمقدار الإزاحة أفضل من لوحة لا
// تُفتح، ومن ٥٠٠ يظنّها التاجر عطلاً في متجره.
//
// ولا يُسجَّل هنا: هذه طبقة حالات الاستخدام ولا مُسجِّل فيها. ما يقع فعلاً هو أن `Resolved` تقول
// ما إذا كانت الإجابة عن المنطقة المطلوبة أم عن UTC، واللوحة تُعلن ذلك في استجابتها — فالتاجر
// الذي يرى أرقاماً لا تطابق يومه يجد السبب في الجواب نفسه لا في سجلّ خادم.
// ============================================================================
public sealed record StoreTimeZone(TimeZoneInfo Zone, bool Resolved)
{
    public static StoreTimeZone Utc => new(TimeZoneInfo.Utc, true);

    public string Id => Zone.Id;

    // ============================================================================
    // الإزاحة الصالحة عند لحظة بعينها. يستعملها تجميعُ المنحنى في SQL (`DATEADD`) لأن
    // `AT TIME ZONE` — وهو الأصحّ نظرياً — لا يُترجمه EF Core 10؛ الحجّة كاملةً وحدُّها الدقيق
    // في رأس `StoreReportQueries.TrendAsync`. الحدود والمجاميع **لا** تمرّ من هنا: تُحسب بـ
    // `ToUtc`/`ToLocal` بـ TimeZoneInfo كاملاً، فتبقى دقيقة عبر التوقيت الصيفي.
    // ============================================================================
    public int OffsetMinutesAt(DateTime utc) => (int)Zone.GetUtcOffset(utc).TotalMinutes;

    public static StoreTimeZone Resolve(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return Utc;
        try
        {
            // .NET 8+ يقبل معرّفات IANA على كل المنصّات (ICU)، فلا حاجة لجدول تحويل هنا.
            return new StoreTimeZone(TimeZoneInfo.FindSystemTimeZoneById(ianaId), true);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return new StoreTimeZone(TimeZoneInfo.Utc, false);
        }
    }

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

    // ============================================================================
    // من وقتٍ محلّي إلى UTC، مع الحالتين اللتين تكسران التحويل الساذج:
    //
    //   • **وقتٌ غير موجود**: ليلة التقديم الصيفي تقفز الساعة، وفي مناطق تقفزها عند منتصف الليل
    //     (Lord Howe وبعض مناطق أمريكا الجنوبية تاريخياً) لا يوجد "منتصف ليل" ذلك اليوم أصلاً،
    //     و`ConvertTimeToUtc` **ترمي**. لوحة تسقط مرّة في السنة لسبب لا يخطر لأحد هي أسوأ أنواع
    //     العطل. نتقدّم بالساعة التي قفزت فنأخذ أول لحظة موجودة فعلاً من ذلك اليوم.
    //   • **وقتٌ ملتبس**: ليلة التأخير الصيفي تتكرّر الساعة، فلمنتصف الليل تأويلان. نختار
    //     **الأوّل** (الإزاحة قبل التغيير) كي لا تُفقد ساعة من أوّل اليوم.
    // ============================================================================
    public DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);

        return Zone.IsAmbiguousTime(unspecified)
            ? unspecified - Zone.GetAmbiguousTimeOffsets(unspecified).Max()
            : TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }
}
