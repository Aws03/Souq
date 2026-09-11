namespace Souq.API.Tenancy;

// ============================================================================
// إعداد تحديد المستأجر (ADR-0006). الإنتاج: خريطة TenantDomains وحدها (+ مضيفو المنصّة من هنا).
// التطوير/الاختبار فقط: localhost ⇒ LocalDefaultTenant، و{slug}.localhost، وترويسة X-Tenant.
// ============================================================================
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";
    public const string DevelopmentTenantHeader = "X-Tenant";
    public const string DevelopmentPlatformHost = "admin.localhost";

    // مضيفو منطقة المنصّة (مثل admin.souq.app): لا متجر عليها — نقاط [PlatformEndpoint] فقط.
    public string[] PlatformHosts { get; set; } = [];

    // Development/Testing فقط: معرّف المتجر الذي يخدمه localhost/127.0.0.1 مباشرة — افتراضياً متجر البذر
    // (DbSeeder.DefaultTenantSlug، في Program)؛ الضبط هنا يختار متجراً آخر.
    public string? LocalDefaultTenant { get; set; }

    // يُحسب من البيئة في Program (PostConfigure) ويطغى على أي قيمة في الإعداد: ترويسة يختار بها
    // العميل متجره مقبولة في التطوير فقط — في الإنتاج هي عبور مستأجرين بسطر واحد.
    public bool AllowDevelopmentResolution { get; set; }
}
