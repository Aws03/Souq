using System.Text.RegularExpressions;
using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Platform;

// ============================================================================
// Tenant — متجر واحد على المنصّة: جذر تجمّع وحدة Platform (Modules.md). يملك نطاقاته ودورة
// حياته ولغته وعملته ومنطقته الزمنية. لا يعرف شيئاً عن المنتجات أو الطلبات: كل صف تجاري
// يشير إليه بـ TenantId فقط (مفتاح أجنبي من النواة المشتركة — Architecture.md §6).
//
// قواعد التجمّع: انتقالات الحالة محروسة؛ نطاق أساسي واحد بالضبط متى وُجدت نطاقات؛ المعرّف
// (slug) ثابت بعد الإنشاء لأنه جزء من المضيف الفرعي والروابط الخارجية.
// ============================================================================
public partial class Tenant : Entity
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 40;
    public const int TimeZoneMaxLength = 64;

    // لغات الواجهة المدعومة اليوم؛ جداول الترجمة (المرحلة 5) تتّسع لغيرها دون تغيير هنا.
    public static readonly IReadOnlyList<string> SupportedCultures = ["ar", "en"];

    // معرّفات محجوزة: تتعارض مع مضيف المنصّة (admin.localhost) أو مسارات النظام.
    private static readonly HashSet<string> ReservedSlugs =
        new(StringComparer.Ordinal) { "admin", "api", "app", "platform", "static", "uploads", "www" };

    private readonly List<TenantDomain> _domains = new();

    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public TenantStatus Status { get; private set; }
    public string DefaultCulture { get; private set; } = default!;
    public string Currency { get; private set; } = default!;
    public string TimeZone { get; private set; } = default!;

    public IReadOnlyCollection<TenantDomain> Domains => _domains.AsReadOnly();

    private Tenant() { }

    public Tenant(string name, string slug, string currency, string defaultCulture, string timeZone)
    {
        Rename(name);
        Slug = NormalizeSlug(slug);
        Currency = NormalizeCurrency(currency);
        SetLocale(defaultCulture, timeZone);
        Status = TenantStatus.Provisioning;
    }

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is < 2 or > NameMaxLength)
            throw new InvalidTenantOperationException($"اسم المتجر يجب أن يكون بين 2 و{NameMaxLength} حرفاً");
        Name = trimmed;
    }

    public void SetLocale(string defaultCulture, string timeZone)
    {
        var culture = defaultCulture?.Trim().ToLowerInvariant() ?? "";
        if (!SupportedCultures.Contains(culture))
            throw new InvalidTenantOperationException($"لغة غير مدعومة: {defaultCulture}");
        var zone = timeZone?.Trim() ?? "";
        if (zone.Length > TimeZoneMaxLength || !TimeZonePattern().IsMatch(zone))
            throw new InvalidTenantOperationException($"منطقة زمنية غير صالحة: {timeZone}");
        DefaultCulture = culture;
        TimeZone = zone;
    }

    // تغيير العملة يكسر التاريخ المالي (أسعار وطلبات بعملة سابقة) — مسموح فقط قبل أي نشاط
    // تجاري. "هل يوجد نشاط؟" سؤال قاعدة بيانات يجيب عنه المستدعي (WhiteLabel.md §2).
    public void ChangeCurrency(string currency, bool hasCommercialActivity)
    {
        var code = NormalizeCurrency(currency);
        if (code == Currency) return;
        if (hasCommercialActivity)
            throw new InvalidTenantOperationException("لا يمكن تغيير عملة متجر بعد إضافة منتجات أو طلبات");
        Currency = code;
    }

    public void Activate()
    {
        if (Status == TenantStatus.Archived)
            throw new InvalidTenantOperationException("المتجر المؤرشف لا يُعاد تفعيله");
        Status = TenantStatus.Active;
    }

    public void Suspend()
    {
        if (Status != TenantStatus.Active)
            throw new InvalidTenantOperationException("يمكن إيقاف متجر فعّال فقط");
        Status = TenantStatus.Suspended;
    }

    public void Archive()
    {
        if (Status == TenantStatus.Archived)
            throw new InvalidTenantOperationException("المتجر مؤرشف مسبقاً");
        Status = TenantStatus.Archived;
    }

    // أول نطاق يصبح الأساسي تلقائياً — متجر بنطاقات بلا نطاق أساسي حالة مستحيلة هنا.
    public TenantDomain AddDomain(string host)
    {
        var normalized = TenantDomain.NormalizeHost(host);
        if (_domains.Any(d => d.Host == normalized))
            throw new InvalidTenantOperationException($"النطاق {normalized} مضاف لهذا المتجر مسبقاً");

        var domain = new TenantDomain(normalized, isPrimary: _domains.Count == 0);
        _domains.Add(domain);
        return domain;
    }

    public void SetPrimaryDomain(string host)
    {
        var target = FindDomain(host);
        foreach (var domain in _domains)
            domain.SetPrimary(ReferenceEquals(domain, target));
    }

    // لا يُحذف النطاق الأساسي ما دامت نطاقات أخرى قائمة: يُعيَّن غيره أساسياً أولاً.
    public void RemoveDomain(string host)
    {
        var target = FindDomain(host);
        if (target.IsPrimary && _domains.Count > 1)
            throw new InvalidTenantOperationException("عيّن نطاقاً أساسياً آخر قبل حذف النطاق الأساسي");
        _domains.Remove(target);
    }

    private TenantDomain FindDomain(string host)
    {
        var normalized = TenantDomain.NormalizeHost(host);
        return _domains.FirstOrDefault(d => d.Host == normalized)
            ?? throw new InvalidTenantOperationException($"النطاق {normalized} غير مضاف لهذا المتجر");
    }

    public static string NormalizeSlug(string slug)
    {
        var normalized = slug?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length is < 2 or > SlugMaxLength || !SlugPattern().IsMatch(normalized))
            throw new InvalidTenantOperationException("المعرّف يقبل أحرفاً لاتينية صغيرة وأرقاماً وشرطات (2–40 حرفاً)");
        if (ReservedSlugs.Contains(normalized))
            throw new InvalidTenantOperationException($"المعرّف {normalized} محجوز للنظام");
        return normalized;
    }

    private static string NormalizeCurrency(string currency)
    {
        var code = currency?.Trim().ToUpperInvariant() ?? "";
        if (!CurrencyInfo.IsValidCode(code))
            throw new InvalidTenantOperationException($"رمز عملة غير صالح: {currency}");
        return code;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    // معرّف IANA (Asia/Amman, America/Argentina/Buenos_Aires, UTC) — الشكل فقط؛ لا نعتمد على قاعدة
    // مناطق نظام التشغيل داخل المجال.
    [GeneratedRegex(@"^(?:UTC|[A-Za-z_]+(?:/[A-Za-z0-9_+\-]+)+)$")]
    private static partial Regex TimeZonePattern();
}
