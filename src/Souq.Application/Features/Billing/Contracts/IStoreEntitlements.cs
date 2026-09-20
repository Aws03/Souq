namespace Souq.Application.Features.Billing.Contracts;

// ============================================================================
// عقد وحدة Billing تجاه منطقة المنصّة (Modules.md §2). سببه واحد ومحدَّد: تجهيزُ متجرٍ جديد يجب أن
// يمنحه عقداً، وإلّا وُلد بلا وحدة اختيارية واحدة — الاستحقاق يفشل مغلقاً منذ C1.
//
// ولماذا عقدٌ لا استدعاءٌ مباشر؟ لأن البديل كان `CreateTenantHandler` يشير إلى `Plan` و`Subscription`
// و`IPlanRepository` مباشرةً، فيصير **Platform ⇄ Billing** دورةً في اتجاهَي المجال معاً: Billing تقرأ
// `Tenant`، وPlatform تبني `Subscription`. الجرد المولَّد (ModuleDomainDependencies.md) يجعل ذلك
// مرئياً لا مسموعاً، وهذا العقد يزيله: Platform لا تعرف من Billing إلا هذه الطريقة الواحدة.
// ============================================================================
// ملخّص عقد المتجر للعرض على شاشة المنصّة — لا شروطه كاملة. null لمتجر بلا عقد، وهي حالة تُعرض
// صراحةً: مشغّلٌ يرى وحدةً مطفأة بلا سبب معروض سيظنّها عطباً.
public sealed record StorePlanSummary(int PlanId, string Code, string Name, int Version, string Status);

public interface IStoreEntitlements
{
    // يُسنِد الخطة التأسيسية لمتجرٍ جُهّز للتوّ. false إن لم تكن منشورة — وعندها يبقى المتجر بلا
    // عقد، وهي حالةٌ مشروعة تعرضها شاشة المنصّة صراحةً، لا خطأٌ يُفشل التجهيز في منتصفه.
    Task<bool> AssignFoundationPlanAsync(int tenantId, CancellationToken ct = default);

    // ما يسري على هذا المتجر الآن: خطته، وما تمنحه فعلاً. تقرؤه شاشة المتجر في المنصّة كي تشرح
    // لماذا وحدةٌ ما مطفأة — بلا صلاحية فوترة إضافية، فهي جزء من ملفّ المتجر لا من إدارة الخطط.
    Task<StorePlanSummary?> GetPlanSummaryAsync(int tenantId, CancellationToken ct = default);
}
