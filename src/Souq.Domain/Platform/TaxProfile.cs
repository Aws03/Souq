using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Platform;

// ============================================================================
// ملفُّ ضريبةِ اختصاصٍ قضائيّ: قواعدُ بلدٍ أو منطقةٍ تحفظها المنصّة **مرّةً** ويختارها أيُّ متجر
// ([ADR-0055](0055)، قرار المالك P-06).
//
// **لا قاعدةَ ضريبةٍ في الشيفرة.** كلُّ نسبةٍ وعتبةٍ وعُرفٍ (شاملٌ للسعر أم مضافٌ عليه) قيمةٌ في
// إصدارٍ من هذا الملفّ. اسمُ البلد قد يظهر في ملفّ؛ **ولا يظهر في شرطٍ** أبداً.
//
// **والإصدار المنشور لا يُعدَّل.** التصحيحُ إصدارٌ جديد بتاريخ نفاذه، فتبقى قواعدُ يومٍ مضى معروفةً
// من القاعدة وحدها — وهي الطريقة الوحيدة التي يبقى بها طلبُ العام الماضي محتسباً بقواعد العام
// الماضي. النمطُ نفسه الذي تعيش به `Plan`: مسوّدةٌ تُعدَّل، ومنشورٌ يُجمَّد.
//
// **ولا تُجمَع ضريبةٌ إلا بإصدارٍ تحقّق منه مهنيٌّ باسمه.** هذا هو الفرق بين «قيمةٌ في القاعدة»
// و«قيمةٌ يُعتمد عليها»: قرار المالك يقول إنّ ما في الملفّ **إعدادٌ يحتاج تحقّقاً من محاسبٍ أو
// جهةٍ ضريبية أو مستشارٍ قانونيّ قبل الاستخدام التجاري**. فالهندسة لا تضع `Verified` أبداً — لا في
// كود، ولا في هجرة، ولا افتراضاً — ويبقى الرقمُ المبحوثُ عنه غيرَ قادرٍ على أن يصل مشترياً.
// ============================================================================
public class TaxProfile : Entity
{
    public const int JurisdictionMinLength = 2;
    public const int JurisdictionMaxLength = 10;
    public const int NameMinLength = 2;
    public const int NameMaxLength = 120;
    public const int MaxVersions = 200;

    private readonly List<TaxProfileVersion> _versions = new();

    // رمزُ الاختصاص: بلدٌ بـ ISO 3166-1 alpha-2 عادةً، أو رمزُ منطقةٍ حين تكون القاعدة إقليمية.
    // يُطبَّع بحروفٍ كبيرة ولا يُفسَّر: هو مفتاحُ تجميعٍ وعرضٍ لا مصدرُ منطق.
    public string Jurisdiction { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public IReadOnlyCollection<TaxProfileVersion> Versions => _versions.AsReadOnly();

    private TaxProfile() { }

    public TaxProfile(string jurisdiction, string name)
    {
        Jurisdiction = NormalizeJurisdiction(jurisdiction);
        Rename(name);
    }

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length < NameMinLength || trimmed.Length > NameMaxLength)
            throw new InvalidTaxProfileException($"اسم ملفّ الضريبة بين {NameMinLength} و{NameMaxLength} حرفاً");
        if (trimmed.Any(char.IsControl))
            throw new InvalidTaxProfileException("اسم ملفّ الضريبة يحتوي محارف غير مسموحة");
        Name = trimmed;
    }

    // ============================================================================
    // مسوّدةُ إصدارٍ جديد. تاريخُ النفاذ **لا يسبق آخر إصدارٍ منشور**: إصدارٌ ينفذ قبل منشورٍ قائم
    // يعني أنّ قواعدَ مدّةٍ مضت تتغيّر بأثرٍ رجعيّ — وطلباتُ تلك المدّة قد صدرت فواتيرُها.
    //
    // ومسوّدةٌ واحدة في كل وقت: مسوّدتان تجعلان «الإصدار التالي» سؤالاً بلا جواب.
    // ============================================================================
    public TaxProfileVersion AddDraft(DateTime effectiveFrom, TaxPriceMode priceMode, bool shippingTaxable)
    {
        if (_versions.Count >= MaxVersions)
            throw new InvalidTaxProfileException($"حتى {MaxVersions} إصداراً لملفّ الضريبة");
        if (_versions.Any(v => v.Status == TaxProfileVersionStatus.Draft))
            throw new InvalidTaxProfileException("لملفّ الضريبة مسوّدةٌ قائمة — انشرها أو عدّلها");

        var lastPublished = _versions
            .Where(v => v.Status == TaxProfileVersionStatus.Published)
            .OrderByDescending(v => v.EffectiveFrom)
            .FirstOrDefault();
        if (lastPublished is not null && effectiveFrom <= lastPublished.EffectiveFrom)
            throw new InvalidTaxProfileException("تاريخ نفاذ الإصدار يجب أن يتجاوز آخر إصدار منشور");

        var version = new TaxProfileVersion(_versions.Count + 1, effectiveFrom, priceMode, shippingTaxable);
        _versions.Add(version);
        return version;
    }

    public TaxProfileVersion? Draft =>
        _versions.FirstOrDefault(v => v.Status == TaxProfileVersionStatus.Draft);

    // ============================================================================
    // الإصدارُ النافذ في لحظةٍ بعينها — وهو ما يُلتقط في لقطة الطلب، لا مرجعٌ يتحرّك.
    //
    // ويُرجَّح بأحدثِ `EffectiveFrom` لا يتجاوز اللحظة، والمنشورُ وحده يُحتسب: مسوّدةٌ لا تنفذ
    // على أحدٍ، ولو كان تاريخُها في الماضي.
    // ============================================================================
    public TaxProfileVersion? VersionOn(DateTime instant) => _versions
        .Where(v => v.Status == TaxProfileVersionStatus.Published && v.EffectiveFrom <= instant)
        .OrderByDescending(v => v.EffectiveFrom)
        .ThenByDescending(v => v.Version)
        .FirstOrDefault();

    private static string NormalizeJurisdiction(string value)
    {
        var trimmed = value?.Trim().ToUpperInvariant() ?? "";
        if (trimmed.Length < JurisdictionMinLength || trimmed.Length > JurisdictionMaxLength)
            throw new InvalidTaxProfileException($"رمز الاختصاص بين {JurisdictionMinLength} و{JurisdictionMaxLength} محرفاً");
        // حروفٌ وأرقامٌ وشرطة: رموزُ الأقاليم تحمل أرقاماً فعلاً (ISO 3166-2 مثل FR-01)، ومنعُها
        // كان سيرفض رمزاً صحيحاً — أمسكه اختبارُ التكامل قبل أن يُمسكه مستخدم.
        if (!trimmed.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '-'))
            throw new InvalidTaxProfileException("رمز الاختصاص حروفٌ لاتينية وأرقامٌ وشرطة فقط");
        return trimmed;
    }
}

public enum TaxProfileVersionStatus
{
    Draft = 0,
    Published = 1,
}

// ============================================================================
// **العُرف: شاملٌ أم مضاف** — وهو خاصيّةُ إصدارٍ لا ثابتٌ للمنصّة، وهذا هو جوهر جواب P-06.
//
// السؤال كما طُرح كان: «هل أسعار المنصّة شاملةٌ للضريبة أم مضافٌ عليها؟» والجوابُ أنّ المنصّة لا
// تُجيب: يُجيب كلُّ اختصاصٍ عن نفسه. فمنصّةٌ واحدة تخدم سوقاً يعرض الأسعار شاملةً وآخرَ يضيفها
// عند الدفع، بلا فرعٍ في الشيفرة.
//
//   • Exclusive — السعر المعروض قبل الضريبة، وتُضاف عند الدفع فيدفع المشتري أكثر من المعروض.
//   • Inclusive — السعر المعروض هو ما يدفعه، ويُستخرَج منه جزءُ الضريبة للفاتورة.
// ============================================================================
public enum TaxPriceMode
{
    Exclusive = 0,
    Inclusive = 1,
}

// ============================================================================
// حالةُ التحقّق من إصدار. **لا تضعها الهندسة أبداً** — لا في كود ولا في هجرة ولا افتراضاً.
//
//   • Unverified — قيمةٌ مُدخَلة أو مبحوثٌ عنها، لم يؤكّدها أحد. **لا تُجمَع بها ضريبة.**
//   • RequiresProfessionalConfirmation — نُظِر فيها وبقي سؤالٌ لمهنيّ. **لا تُجمَع بها ضريبة.**
//   • Verified — أكّدها مهنيٌّ **باسمه وتاريخه**. وحدَها تُجمَع بها ضريبة.
// ============================================================================
public enum TaxVerificationState
{
    Unverified = 0,
    RequiresProfessionalConfirmation = 1,
    Verified = 2,
}

// مَن تحقّق ومتى وبأيّ ملاحظة. الاسمُ نصٌّ حرّ عمداً: قد يكون مكتب محاسبةٍ أو مستشاراً أو مرجعاً
// من الجهة الضريبية — والكودُ لا يفسّره، إنما يُبرزه حيث يُقرأ الرقم.
public sealed record TaxVerification(TaxVerificationState State, string? By, DateTime? At, string? Note)
{
    public const int ByMaxLength = 120;
    public const int NoteMaxLength = 500;

    public static TaxVerification Unverified { get; } = new(TaxVerificationState.Unverified, null, null, null);

    public bool AllowsCollection => State == TaxVerificationState.Verified;
}

// ============================================================================
// إصدارٌ واحد من قواعد اختصاص: يُعدَّل مسوّدةً، ويُجمَّد منشوراً.
// ============================================================================
public class TaxProfileVersion : Entity
{
    public const int MaxRates = 30;

    private readonly List<TaxRate> _rates = new();

    public int TaxProfileId { get; private set; }
    public int Version { get; private set; }
    public DateTime EffectiveFrom { get; private set; }
    public TaxProfileVersionStatus Status { get; private set; }
    public TaxPriceMode PriceMode { get; private set; }

    // ============================================================================
    // هل يُضرَّب الشحن؟ **قاعدةُ اختصاصٍ لا اختيارٌ هندسيّ**، ولذلك لا قيمةَ افتراضية لها: مَن
    // يُدخل قواعد الاختصاص يُجيب، ويبقى جوابُه غيرَ متحقَّقٍ منه حتى يؤكّده مهنيّ.
    //
    // وافتراضُ أحد الجوابَين كان سيكون أسوأ الاحتمالين: خطأٌ بمقدار ضريبةِ الشحن في كل طلب، في
    // اتجاهٍ لا يظهر إلا في تسويةٍ ضريبية.
    // ============================================================================
    public bool ShippingTaxable { get; private set; }

    public TaxVerification Verification { get; private set; } = TaxVerification.Unverified;

    // ============================================================================
    // عتبةُ التسجيل، إن كان للاختصاص عتبة. **لا يُحتسب بها شيءٌ اليوم**، وهذا مكتوبٌ كي لا يُفترض
    // خلافُه: هي قيمةٌ تُعرَض لمن يقرّر، لا شرطٌ يُطبّقه الكود على مبيعات متجر. ويومَ تُطبَّق، تُطبَّق
    // بقاعدةٍ يكتبها محاسب لا بتخمينٍ من هنا.
    // ============================================================================
    public Money? RegistrationThreshold { get; private set; }

    // ملاحظاتُ الاختصاص لمن يُعِدّ: ما يشترطه على الفاتورة، وما يجب سؤالُ مهنيٍّ عنه. نصٌّ لا يقرؤه
    // الكود — وهو الموضع الذي يُكتب فيه ما لا يجوز للهندسة أن تُحوّله إلى منطق.
    public string? Notes { get; private set; }

    public IReadOnlyCollection<TaxRate> Rates => _rates.AsReadOnly();

    // القاعدة التي يفرضها كلُّ ما سبق: لا تُجمَع ضريبةٌ إلا من إصدارٍ **منشور ومُتحقَّق منه**.
    public bool AllowsCollection =>
        Status == TaxProfileVersionStatus.Published && Verification.AllowsCollection;

    private TaxProfileVersion() { }

    internal TaxProfileVersion(int version, DateTime effectiveFrom, TaxPriceMode priceMode, bool shippingTaxable)
    {
        if (version < 1) throw new InvalidTaxProfileException("إصدار ملفّ الضريبة يبدأ من 1");
        Version = version;
        EffectiveFrom = effectiveFrom;
        PriceMode = priceMode;
        ShippingTaxable = shippingTaxable;
        Status = TaxProfileVersionStatus.Draft;
    }

    public void SetRates(IEnumerable<TaxRate> rates)
    {
        RequireDraft("نسب الإصدار");
        var normalized = (rates ?? []).ToList();
        if (normalized.Count > MaxRates)
            throw new InvalidTaxProfileException($"حتى {MaxRates} نسبةً للإصدار");
        if (normalized.Select(r => r.Code).Distinct(StringComparer.Ordinal).Count() != normalized.Count)
            throw new InvalidTaxProfileException("رمزٌ واحد لكل نسبة");

        _rates.Clear();
        foreach (var rate in normalized.OrderBy(r => r.Code, StringComparer.Ordinal)) _rates.Add(rate);
    }

    public void SetThreshold(Money? threshold)
    {
        RequireDraft("عتبة التسجيل");
        RegistrationThreshold = threshold;
    }

    public void SetNotes(string? notes)
    {
        RequireDraft("ملاحظات الإصدار");
        var trimmed = notes?.Trim();
        if (trimmed is { Length: > 4000 })
            throw new InvalidTaxProfileException("ملاحظات الإصدار حتى 4000 حرفاً");
        Notes = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public void SetPriceMode(TaxPriceMode priceMode)
    {
        RequireDraft("عُرف السعر");
        PriceMode = priceMode;
    }

    public void SetShippingTaxable(bool taxable)
    {
        RequireDraft("ضريبة الشحن");
        ShippingTaxable = taxable;
    }

    // ============================================================================
    // نقاطُ الأساس المنطبقة على فئةٍ بعينها — **مجموعُ** ما ينطبق منها، لأنّ بعض الاختصاصات تفرض
    // ضريبتَين على المبيعة نفسها (وطنيّة وبلدية). والجمعُ هنا صريحٌ كي لا يُفترض أنّ الأولى هي
    // الوحيدة.
    //
    // وفئةٌ لا نسبةَ لها ⇒ صفر: غيابُ نسبةٍ لفئةٍ هو **عدمُ خضوعٍ** لا نقصٌ في الإعداد، وهو الفرق
    // بين «معفى» و«لم يُضبَط». وما لم يُسنَد للمنتج فئةٌ فهو على الفئة الافتراضية (الشريحة التالية).
    // ============================================================================
    public int BasisPointsFor(string category)
    {
        var key = (category ?? TaxRate.DefaultCategory).Trim();
        return _rates.Where(r => string.Equals(r.Category, key, StringComparison.Ordinal)).Sum(r => r.BasisPoints);
    }

    public IReadOnlyList<TaxRate> RatesFor(string category)
    {
        var key = (category ?? TaxRate.DefaultCategory).Trim();
        return _rates.Where(r => string.Equals(r.Category, key, StringComparison.Ordinal)).ToList();
    }

    // النشرُ تجميد. وإصدارٌ بلا نسبةٍ واحدة لا يُنشر: إصدارٌ لا يُحتسب منه شيءٌ ليس قاعدةً، وهو
    // يُشبه في القاعدة إصداراً صالحاً فيُختار ولا يفعل شيئاً.
    public void Publish()
    {
        RequireDraft("نشر الإصدار");
        if (_rates.Count == 0)
            throw new InvalidTaxProfileException("لا يُنشر إصدارٌ بلا نسبة واحدة");
        Status = TaxProfileVersionStatus.Published;
    }

    // ============================================================================
    // التحقّق فعلُ إنسان: يُسمّي نفسه ويترك تاريخَه. ولا يُقبَل على مسوّدة — التحقّق من قيمٍ قد
    // تتغيّر قبل النشر لا يعني شيئاً.
    //
    // وأيُّ تعديلٍ لاحق مستحيل بالبناء (المنشور مجمَّد)، فالتحقّق يخصّ **هذه** القيم لا غيرها.
    // ============================================================================
    public void Verify(string by, DateTime utcNow, string? note)
    {
        if (Status != TaxProfileVersionStatus.Published)
            throw new InvalidTaxProfileException("لا يُتحقَّق من مسوّدة — انشرها أولاً");

        var verifier = by?.Trim() ?? "";
        if (verifier.Length is 0 or > TaxVerification.ByMaxLength)
            throw new InvalidTaxProfileException($"اسم المتحقّق مطلوب، حتى {TaxVerification.ByMaxLength} حرفاً");
        var trimmedNote = note?.Trim();
        if (trimmedNote is { Length: > TaxVerification.NoteMaxLength })
            throw new InvalidTaxProfileException($"ملاحظة التحقّق حتى {TaxVerification.NoteMaxLength} حرفاً");

        Verification = new TaxVerification(TaxVerificationState.Verified, verifier, utcNow, trimmedNote);
    }

    // سحبُ التحقّق: يعود الإصدار إلى «يحتاج تأكيداً مهنياً» فتتوقّف الجمعُ به فوراً. لا يُحذف
    // الإصدار ولا يُعدَّل — الطلباتُ التي احتُسبت به تحمل لقطتَها، وتاريخُها لا يتغيّر بهذا.
    public void RequireConfirmation(string? note)
    {
        var trimmed = note?.Trim();
        if (trimmed is { Length: > TaxVerification.NoteMaxLength })
            throw new InvalidTaxProfileException($"ملاحظة التحقّق حتى {TaxVerification.NoteMaxLength} حرفاً");
        Verification = new TaxVerification(
            TaxVerificationState.RequiresProfessionalConfirmation, Verification.By, Verification.At, trimmed);
    }

    private void RequireDraft(string what)
    {
        if (Status != TaxProfileVersionStatus.Draft)
            throw new InvalidTaxProfileException($"{what} لا يُعدَّل بعد النشر — أنشئ إصداراً جديداً بتاريخ نفاذه");
    }
}

// ============================================================================
// نسبةٌ داخل إصدار، **بنقاط الأساس** لا بعددٍ عشريّ: 1600 نقطة = 16%. النقاطُ عددٌ صحيح، فلا
// يقترب حسابُ الضريبة من الفاصلة العائمة من أيّ جهة — والتقريبُ يبقى في موضعٍ واحدٍ مسموح به
// (`Money`)، كما تفرض ADR-0014.
//
// و`Category` هي الفئةُ التي تنطبق عليها النسبة. اليوم فئةٌ واحدة افتراضية، وإسنادُ فئةٍ لمنتجٍ
// هو الشريحة التالية — مكتوبٌ كي لا يُفترض أنه موجود.
// ============================================================================
public class TaxRate : Entity
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 120;
    public const int CategoryMaxLength = 40;
    public const int MaxBasisPoints = 10_000;

    // الفئة الافتراضية: ما لم يُسنَد للمنتج غيرُها. اسمٌ لا قاعدة.
    public const string DefaultCategory = "standard";

    public int TaxProfileVersionId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public int BasisPoints { get; private set; }
    public string Category { get; private set; } = default!;

    private TaxRate() { }

    public TaxRate(string code, string name, int basisPoints, string? category = null)
    {
        Code = Normalize(code, CodeMaxLength, "رمز النسبة");
        Name = Normalize(name, NameMaxLength, "اسم النسبة");
        if (basisPoints is < 0 or > MaxBasisPoints)
            throw new InvalidTaxProfileException($"نقاط الأساس بين 0 و{MaxBasisPoints} (100%)");
        BasisPoints = basisPoints;
        Category = Normalize(category ?? DefaultCategory, CategoryMaxLength, "فئة النسبة");
    }

    private static string Normalize(string value, int maxLength, string field)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length is 0 || trimmed.Length > maxLength)
            throw new InvalidTaxProfileException($"{field} مطلوب، حتى {maxLength} حرفاً");
        if (trimmed.Any(char.IsControl))
            throw new InvalidTaxProfileException($"{field} يحتوي محارف غير مسموحة");
        return trimmed;
    }
}
