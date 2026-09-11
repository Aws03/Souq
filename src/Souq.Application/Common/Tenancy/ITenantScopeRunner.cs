namespace Souq.Application.Common.Tenancy;

// ============================================================================
// ITenantScopeRunner — لمسار المنصّة حين يكتب داخل متجر بعينه (دعوة مديره، رفع شعاره): نطاق خدمات جديد بسياق
// ذلك المتجر — DbContext جديد يختم حارسُ الكتابة فيه متجرَه، والتخزين تحت بادئته. لا "تبديل" للسياق القائم
// (يُضبط مرّة واحدة). TService يُحلّ من النطاق الجديد، وينتهي العمل بانتهائه.
// ============================================================================
public interface ITenantScopeRunner
{
    Task<TResult> RunAsync<TService, TResult>(TenantInfo tenant, Func<TService, Task<TResult>> work)
        where TService : notnull;
}
