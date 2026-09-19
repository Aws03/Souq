using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;

namespace Souq.API.Security;

// ============================================================================
// ما يُفحص بعد صحّة توقيع التوكن وعمره (JwtBearerEvents.OnTokenValidated) — فشل أيٍّ منهما ⇒ التوكن
// غير صالح ⇒ 401 لكل نقطة محمية:
//   1) الربط بالمضيف (ADR-0006): tid يطابق متجر المضيف؛ على مضيف المنصّة لا tid إطلاقاً. بلا هذا،
//      مدير متجر A يعيد استخدام توكنه على B (المرشّحات تحمي البيانات أصلاً؛ هذا يمنع حتى المحاولة).
//   2) ختم الأمان (ADR-0010): sstamp يطابق ختم الحساب الحالي والحساب فعّال — تغيير كلمة المرور،
//      التعطيل، أو كشف سرقة رمز تجديد يُسقط كل توكنات الوصول فوراً لا بعد انتهائها.
// ============================================================================
public static class AccessTokenValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var services = context.HttpContext.RequestServices;
        var tenancy = services.GetRequiredService<ITenantContext>();

        var tokenTenant = principal?.FindFirst(SouqClaimTypes.TenantId)?.Value;
        var boundToHost = tenancy.Scope switch
        {
            TenantScope.Tenant => tokenTenant == tenancy.Tenant!.Id.ToString(CultureInfo.InvariantCulture),
            TenantScope.Platform => tokenTenant is null,
            _ => false,
        };
        if (!boundToHost)
        {
            context.Fail("The token was issued for a different store.");
            return;
        }

        var stamp = principal?.FindFirst(SouqClaimTypes.SecurityStamp)?.Value;
        bool current;
        try
        {
            current = int.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
                      && !string.IsNullOrEmpty(stamp)
                      && await services.GetRequiredService<ISessionValidator>()
                          .IsCurrentAsync(userId, stamp, context.HttpContext.RequestAborted);
        }
        catch (Exception) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // ====================================================================
            // العميل أغلق الاتصال قبل أن يُقرأ ختم الأمان (تنقّل، إغلاق تبويب، انقطاع شبكة).
            //
            // فحص الختم يقرأ القاعدة بـ RequestAborted نفسه، فالإلغاء يخرج استثناءً من هذا
            // المعالج. وبلا التقاطه هنا يبتلعه `JwtBearerHandler` ويكتب **ERROR بمكدّسه**
            // ("Exception occurred while processing message") — أي أنّ كل صفحةٍ يهجرها مستخدمٌ
            // داخلٌ تُنتج خطأ خادم في السجلّ.
            //
            // ── والشرط هو حالة الطلب، لا نوع الاستثناء ──
            // أوّل إصلاح لهذا اصطاد `OperationCanceledException` وحده، **ولم يُغيّر شيئاً**: الإلغاء
            // لا يصل بهذا النوع من طبقة القاعدة. قِيس على الحزمة بعد الإصلاح: 45 سطر ERROR في
            // أربع عشرة دقيقة، كلّها `InvalidOperationException` تلفّ `SqlException` نصّها
            // "the batch is aborted … Operation cancelled by user". فالسؤال الصحيح ليس «أيّ
            // استثناءٍ هذا» بل «هل ما يزال هناك من ينتظر جواباً».
            //
            // ثمنُه مقبولٌ ومقصود: عطلٌ حقيقي يصادف لحظةَ انصراف العميل يُسجَّل انصرافاً. وهو أهون
            // من معدّل أخطاءٍ مصطنع يُنذَر عنه فيُخفي الأعطال الحقيقية — لا أحد يستلم هذا الجواب
            // أصلاً، فلا مستخدم تضرّر ليُحقَّق في أمره.
            //
            // ويُفشَل لا يُتجاوَز: ختمٌ لم يُقرأ ليس ختماً صالحاً (ADR-0010). الإغلاق الآمن هو
            // ألّا تُقبل جلسةٌ لم تُتحقَّق.
            // ====================================================================
            context.Fail("The request was aborted before the session could be checked.");
            return;
        }

        if (!current)
            context.Fail("The session is no longer valid.");
    }
}
