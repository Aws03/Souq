namespace Souq.Application.Common.Notifications;

// ============================================================================
// منفذ مزوّد البريد (المرحلة 14): رسالة مركّبة جاهزة ⇒ مزوّد. يرمي EmailDeliveryException عند الفشل فيعيد المُرسِل الخلفي
// المحاولة — لا يبتلع الخطأ كما كان حين كان البريد في مسار الطلب. لا يستدعيه إلا معالجو صندوق الصادر (اختبار معماري): مسار
// الطلب لا ينتظر مزوّد البريد أبداً.
// ============================================================================
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

// FromName: هوية المرسِل لكل متجر (اسمه المعروض)؛ العنوان عنوان النشر المُتحقَّق منه لدى المزوّد، والردّ يذهب لبريد تواصل
// المتجر (ReplyTo). Kind وActionUrl لسجلّ التطوير وللاختبارات — لا يُسجَّلان خارج التطوير (الرابط يحمل رمزاً).
public sealed record EmailMessage(
    string To, string Subject, string HtmlBody, string TextBody, string FromName, string? ReplyTo, string Kind, string? ActionUrl);

public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

public enum EmailTemplate
{
    PasswordReset, EmailVerification, Invitation, OrderConfirmed, OrderShipped, OrderDelivered, OrderCancelled,
}

// هوية المتجر في الرسالة: الاسم المعروض بلغة الرسالة، الشعار (رابط مطلق)، لونا الترويسة، وبريد التواصل.
public sealed record EmailBranding(string StoreName, string? LogoUrl, string PrimaryColor, string OnPrimaryColor, string? ContactEmail);

// Values: قيم القالب (رقم الطلب، الإجمالي، اسم الجهة الداعية…) نصوصاً خاماً — القالب يرمّزها لـ HTML.
public sealed record EmailContent(
    EmailTemplate Template, string Culture, EmailBranding Branding, string? ActionUrl, IReadOnlyDictionary<string, string> Values);

public sealed record ComposedEmail(string Subject, string HtmlBody, string TextBody);

// قوالب مترجمة بهوية المتجر — عرضٌ، فالتنفيذ في Infrastructure.
public interface IEmailComposer
{
    ComposedEmail Compose(EmailContent content);
}

// أصل واجهة متجر لروابط رسائل لا طلب HTTP خلفها (أحداث الطلب من البوّابة أو المنسّق): نطاقه الأساسي بمخطّط الواجهة ومنفذها.
public interface IStoreOrigins
{
    Task<string> ForStoreAsync(int tenantId, CancellationToken ct);
}
