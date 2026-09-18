namespace Souq.Application.Common.Interfaces;

// ============================================================================
// أصل الواجهة لروابط البريد: مضيف الطلب نفسه (مخطّط + مضيف + منفذ كما رآها المتصفّح) — عميل المتجر B يعود إلى متجر B،
// ومستخدم المنصّة إلى المنصّة. آمن من "تسميم الرابط" بترويسة Host مزيّفة: مضيف غير مسجَّل لمتجر يُرفض بـ 404 قبل أي حالة
// استخدام (TenantResolutionMiddleware). الأصل يُلتقط وقت الطلب ويُحفظ مع رسالة صندوق الصادر؛ الرابط الكامل برمزه يُبنى
// لحظة الإرسال (المرحلة 14). التنفيذ في طبقة الـ API.
// ============================================================================
public interface IStorefrontLinks
{
    // host: مضيف صفحة القبول — نطاق المتجر حين تدعو المنصّة مديره (الطلب على مضيف المنصّة)؛ null ⇒ مضيف الطلب نفسه.
    string Origin(string? host = null);
}

// روابط صفحات الواجهة من أصلها — مكان واحد للمسارات التي تصل بالبريد.
public static class StorefrontLinks
{
    public static string PasswordReset(string origin, string token) => WithToken(origin, "/reset-password", token);

    // استعادة الحساب بلا رمز (M15): وجهة زرّ إشعار "تغيّرت كلمة مرورك" — من لم يُغيّرها يبدأ من هنا.
    public static string ForgotPassword(string origin) => $"{origin.TrimEnd('/')}/forgot-password";

    public static string EmailVerification(string origin, string token) => WithToken(origin, "/verify-email", token);

    public static string Invitation(string origin, string token) => WithToken(origin, "/accept-invitation", token);

    // صفحة التتبّع العامة (المرحلة 9) — رمز التتبّع سرّ حامله، لذا لا يُسجَّل الرابط أبداً.
    public static string OrderTracking(string origin, string trackingToken) =>
        $"{origin.TrimEnd('/')}/track/{Uri.EscapeDataString(trackingToken)}";

    private static string WithToken(string origin, string path, string token) =>
        $"{origin.TrimEnd('/')}{path}?token={Uri.EscapeDataString(token)}";
}
