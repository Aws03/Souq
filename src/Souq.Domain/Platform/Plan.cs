using System.Text.RegularExpressions;
using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Platform;

// حالة إصدار الخطة. الخطة المنشورة **لا تتغيّر أبداً**: المشترك يحتفظ بالشروط التي اشترك عليها،
// فتعديل الشروط إصدارٌ جديد لا تحرير للقائم (ADR-0047 §4: الخطة مُصدَّرة).
public enum PlanStatus
{
    Draft = 0,        // تُحرَّر ولا يُشترك عليها
    Published = 1,    // تُشترك عليها ولا تُحرَّر
    Retired = 2,      // اشتراك جديد ممنوع؛ المشتركون القائمون يبقون عليها
}

// ============================================================================
// Plan — كتالوج الشرائح: جدول منصّة **عالمي** بلا متجر (الشكل C في ADR-0047 §1)، فهو يعيش في
// Souq.Domain.Platform ولا يحمل TenantId. مملوك لوحدة Billing (ModuleMap.DomainOwners) — والنطاق
// يُقارَن بالمساواة في TenancyRuleTests، فنطاق فرعي Souq.Domain.Platform.Billing كان سيُلزم كل نوع
// فيه بـ ITenantOwned ويكسر البناء.
//
// الهوية (Code, Version): "الخطة" اسمٌ ثابت يراه العميل، و"الإصدار" هو ما يُشترك عليه فعلاً.
// ============================================================================
public partial class Plan : Entity
{
    public const int CodeMinLength = 2;
    public const int CodeMaxLength = 40;
    public const int NameMinLength = 2;
    public const int NameMaxLength = 100;
    public const int MaxEntitlements = 50;
    public const int MaxLimits = 50;

    // ============================================================================
    // الخطة التأسيسية — **ليست شريحة تجارية**. هي الخطة التي تحمل ما كان المتجر يملكه قبل أن توجد
    // الخطط أصلاً: الوحدات الاختيارية الثلاث المجّانية اليوم، لا أكثر. وجودها هو ما يجعل C1 لا
    // يغيّر سلوك أيّ متجر قائم: الهجرة تُنشئها وتُسنِدها لكل متجر، والبذر يُنشئها للمتجر الافتراضي،
    // والتجهيز يُسندها للمتجر الجديد.
    //
    // وهي **لا تجيب** قرار المالك C-12 (ما الشرائح وما حدودها): لا سعر لها ولا حدود، ولا تمنح أيّ
    // قدرة مدفوعة — فاستحقاقٌ مدفوع يُضاف لاحقاً لا تمنحه، لأن الخطط تسمّي ما تمنحه. يوم تُوجد
    // شرائح حقيقية، يصير التجهيز يأخذ معرّف خطة ويزول هذا الثابت.
    // ============================================================================
    public const string FoundationCode = "foundation";

    private readonly List<PlanEntitlement> _entitlements = new();
    private readonly List<PlanLimit> _limits = new();

    // الفاصلُ الأدنى والأقصى بالأشهر: شهريٌّ إلى خمسيّ سنوات. ليسا شريحةً ولا سعراً — هما مدى
    // ما تستطيع الآلةُ التعبير عنه، والقيمةُ داخلَه قرارُ المشغّل.
    public const int MinBillingIntervalMonths = 1;
    public const int MaxBillingIntervalMonths = 60;

    public string Code { get; private set; } = default!;
    public int Version { get; private set; }
    public string Name { get; private set; } = default!;
    public PlanStatus Status { get; private set; }

    // ============================================================================
    // سعرُ إصدار الخطة ودورتُه (C5، [ADR-0056](0056)). `null` ⇒ **بلا سعر**: خطةٌ مجّانية أو خطةٌ
    // لم يُسعَّر إصدارُها بعد — والخطةُ التأسيسية منهما، فلا سعرَ لها ولا يُفترض لها.
    //
    // **ووجودُ الحقل ليس جواباً عن `C-12`.** السؤالُ المفتوح هو ما الشرائح وما قيمُها؛ وهذا
    // مكانُ القيمة لا القيمة. ولا يُكتب فيه شيءٌ من هجرةٍ ولا من بذر — يكتبه المشغّل من شاشته،
    // كما لا يكتب المهندسُ نسبةَ ضريبة (ADR-0055).
    //
    // **ويُجمَّد بالنشر** كبقيّة شروط الخطة: مشتركٌ اشترى بسعرٍ لا يتغيّر سعرُه بتحريرِ صفّ —
    // تغييرُ السعر إصدارٌ جديد، وهو ما تعنيه «الخطةُ مُصدَّرة» أصلاً.
    // ============================================================================
    public Money? Price { get; private set; }

    public int BillingIntervalMonths { get; private set; } = MinBillingIntervalMonths;

    public IReadOnlyCollection<PlanEntitlement> Entitlements => _entitlements.AsReadOnly();
    public IReadOnlyCollection<PlanLimit> Limits => _limits.AsReadOnly();

    // ما تمنحه هذه الخطة. الخطة الفارغة تمنح **لا شيء** — وهو الافتراضي المقصود.
    public IReadOnlySet<string> Grants =>
        new HashSet<string>(_entitlements.Select(e => e.Entitlement), StringComparer.Ordinal);

    private Plan() { }

    public Plan(string code, int version, string name)
    {
        Code = NormalizeCode(code);
        if (version < 1) throw new InvalidPlanException("إصدار الخطة يبدأ من 1");
        Version = version;
        Rename(name);
        Status = PlanStatus.Draft;
    }

    public void Rename(string name)
    {
        RequireDraft("اسم الخطة");
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length < NameMinLength || trimmed.Length > NameMaxLength)
            throw new InvalidPlanException($"اسم الخطة بين {NameMinLength} و{NameMaxLength} حرفاً");
        if (trimmed.Any(char.IsControl))
            throw new InvalidPlanException("اسم الخطة يحتوي محارف غير مسموحة");
        Name = trimmed;
    }

    // تستبدل المجموعة كاملةً. مفتاح غير معروف **يرفض** ولا يُتجاهل: الخطة عقد، وتجاهل ما فيها
    // بصمت يعني بيع قدرة لا تُمنح (عكس القراءة المتسامحة في StoreModules.Parse، ولها سببها هناك).
    public void SetEntitlements(IEnumerable<string> entitlements)
    {
        RequireDraft("استحقاقات الخطة");
        // PlanEntitlement يطبّع المفتاح ويرفض المجهول في مُنشئه، فالبناء أولاً ثم إزالة التكرار.
        var normalized = (entitlements ?? []).Select(key => new PlanEntitlement(key).Entitlement)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count > MaxEntitlements)
            throw new InvalidPlanException($"حتى {MaxEntitlements} استحقاقاً للخطة");

        _entitlements.Clear();
        foreach (var key in normalized) _entitlements.Add(new PlanEntitlement(key));
    }

    public void SetLimits(IEnumerable<Limit> limits)
    {
        RequireDraft("حدود الخطة");
        var normalized = (limits ?? []).ToList();
        if (normalized.Select(l => l.Name).Distinct(StringComparer.Ordinal).Count() != normalized.Count)
            throw new InvalidPlanException("حدّ واحد لكل اسم");
        if (normalized.Count > MaxLimits)
            throw new InvalidPlanException($"حتى {MaxLimits} حدّاً للخطة");

        _limits.Clear();
        foreach (var limit in normalized.OrderBy(l => l.Name, StringComparer.Ordinal))
            _limits.Add(new PlanLimit(limit.Name, limit.Value));
    }

    // ============================================================================
    // تسعيرُ المسوّدة. السعرُ **بعملة فوترة المنصّة** لا بعملة أيّ متجر — يتحقّق المُنادي من ذلك،
    // ولا يُكتب رمزُ عملةٍ هنا (قاعدةُ الواجهة البيضاء، ويحرسها `WhiteLabelSourceTests`).
    //
    // وسعرُ صفرٍ مقبولٌ وليس كـ`null`: «خطةٌ سعرُها صفر» قرارٌ صريح تُصدَر له فاتورةٌ بصفر
    // وتُغلَق، و«بلا سعر» تعني أنّ أحداً لم يقرّر بعد — والفرقُ بينهما هو ما يمنع فاتورةً تصدر
    // عن خطةٍ لم تُسعَّر.
    // ============================================================================
    public void SetPrice(Money? price, int billingIntervalMonths)
    {
        RequireDraft("سعرُ الخطة");
        if (billingIntervalMonths is < MinBillingIntervalMonths or > MaxBillingIntervalMonths)
            throw new InvalidPlanException(
                $"دورةُ الفوترة بين {MinBillingIntervalMonths} و{MaxBillingIntervalMonths} شهراً");
        Price = price;
        BillingIntervalMonths = billingIntervalMonths;
    }

    // النشر يُجمّد الشروط. لا نمنع خطةً بلا استحقاقات: "خطة بلا وحدات اختيارية" شريحة مشروعة.
    public void Publish()
    {
        if (Status != PlanStatus.Draft)
            throw new InvalidPlanException("تُنشر المسوّدة وحدها");
        Status = PlanStatus.Published;
    }

    public void Retire()
    {
        if (Status != PlanStatus.Published)
            throw new InvalidPlanException("تُتقاعد الخطة المنشورة وحدها");
        Status = PlanStatus.Retired;
    }

    // الحدّ الغائب ليس صفراً وليس لا نهاية: غير محدَّد. ما يعنيه الغياب قرار الفرض (C2) لا قرار الكتالوج.
    public int? LimitFor(string name) =>
        _limits.FirstOrDefault(l => l.Name == Limit.NormalizeName(name))?.Value;

    private void RequireDraft(string what)
    {
        if (Status != PlanStatus.Draft)
            throw new InvalidPlanException($"لا يُعدَّل {what} بعد النشر — الشروط المشترَك عليها لا تتغيّر؛ أنشئ إصداراً جديداً");
    }

    public static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length < CodeMinLength || normalized.Length > CodeMaxLength || !CodePattern().IsMatch(normalized))
            throw new InvalidPlanException($"معرّف الخطة يقبل أحرفاً لاتينية صغيرة وأرقاماً وشرطات ({CodeMinLength}–{CodeMaxLength} حرفاً)");
        return normalized;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex CodePattern();
}

// استحقاق واحد في إصدار خطة. جدول مرتبط لا عمود نصّي: مجموعة الاستحقاقات تتجاوز عمود الـ 200 حرف
// الذي تحمله Tenants.EnabledModules، وهي تُستعلَم بداخلها (ADR-0047 §4).
public class PlanEntitlement : Entity
{
    public string Entitlement { get; private set; } = default!;

    private PlanEntitlement() { }

    internal PlanEntitlement(string entitlement) => Entitlement = Entitlements.Normalize(entitlement);
}

// حدّ رقمي واحد في إصدار خطة. C1 يحمله ولا يفرضه — الفرض والعدّاد في C2 (ADR-0049).
public class PlanLimit : Entity
{
    public string Name { get; private set; } = default!;
    public int Value { get; private set; }

    private PlanLimit() { }

    internal PlanLimit(string name, int value)
    {
        Name = Limit.NormalizeName(name);
        Value = value >= 0 ? value : throw new InvalidPlanException("قيمة الحدّ لا تكون سالبة");
    }
}
