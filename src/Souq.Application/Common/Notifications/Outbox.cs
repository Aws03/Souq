using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Souq.Domain.Enums;
using Souq.Domain.Events;

namespace Souq.Application.Common.Notifications;

// ============================================================================
// صندوق الصادر (المرحلة 14، D-14، ADR-0034). حالة الاستخدام لا ترسل بريداً ولا تنتظر مزوّداً: تضع رسالة في الصندوق داخل
// وحدة العمل نفسها (تُحفظ مع التغيير أو لا تُحفظ أبداً)، والمُرسِل الخلفي يعالجها لاحقاً بإعادة محاولة. الرسائل مراجع لا
// أسرار ولا بيانات شخصية: معرّف حساب أو طلب + أصل الواجهة لبناء الرابط. رمز إعادة التعيين أو التأكيد أو الدعوة يُولَّد لحظة
// الإرسال، فلا يُخزَّن رمز خام في أي جدول، والبريد يُقرأ من الحساب نفسه عند المعالجة.
// ============================================================================
public interface INotificationOutbox
{
    // يُحفظ مع SaveChanges التالي في وحدة العمل الحالية — لا حفظ منفصل.
    void Enqueue(object message);
}

// Origin: أصل الواجهة (مخطّط + مضيف + منفذ) الذي جاء منه الطلب — الرابط يعود بصاحبه إلى المتجر (أو المنصّة) نفسه.
public sealed record PasswordResetRequested(int UserId, string Origin);
public sealed record EmailVerificationRequested(int UserId, string Origin);
public sealed record AccountInvited(int UserId, string InviterName, string Origin);

// تغيّرت كلمة مرور الحساب (M15، ASVS 2.2.3): يُخطَر صاحبه دائماً، غيّرها بنفسه أو أُعيد تعيينها
// برابط. لا رمز فيها ولا فعلٌ مطلوب — قيمتها كلّها في أن يصل الخبر لمن **لم** يكن هو من غيّرها.
public sealed record PasswordChanged(int UserId, string Origin);

// بريد العميل عن حالة طلبه — رسالة مستقلّة عن إنشاء الإشعارات، فيُعاد البريد وحده إن فشل المزوّد.
public sealed record OrderEmailRequested(int OrderId, OrderStatus Status);

// الأنواع المسموح بها في الصندوق: الاسم المخزَّن يُحلّ من هذه القائمة وحدها — لا تحميل نوع اعتباطي باسم قادم من القاعدة.
public static class NotificationMessageTypes
{
    private static readonly IReadOnlyDictionary<string, Type> ByName = new[]
    {
        typeof(PasswordResetRequested), typeof(EmailVerificationRequested), typeof(AccountInvited), typeof(OrderEmailRequested),
        typeof(PasswordChanged),
        typeof(OrderStatusChanged), typeof(StockBecameLow),
    }.ToDictionary(t => t.Name, StringComparer.Ordinal);

    // الحالات بأسمائها لا بأرقامها: إعادة ترتيب تعداد لا تغيّر معنى رسالة تنتظر في الصندوق. النصوص العربية (اسم الجهة الداعية)
    // كما هي لا \uXXXX — لا تستهلك حدّ الحمولة ستّة أضعاف.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string NameOf(Type type) =>
        ByName.TryGetValue(type.Name, out var known) && known == type
            ? type.Name
            : throw new InvalidOperationException($"{type.FullName} ليس رسالة صندوق صادر مسجّلة");

    public static string Serialize(object message) => JsonSerializer.Serialize(message, message.GetType(), Json);

    public static object? Deserialize(string name, string payload) =>
        ByName.TryGetValue(name, out var type) ? JsonSerializer.Deserialize(payload, type, Json) : null;
}

// معالج رسالة واحدة داخل نطاق متجرها (أو المنصّة). يرمي عند الفشل ⇒ يُعاد بسياسة OutboxRetryPolicy؛ نجاحه ⇒ تُعلَّم منجزة.
// التسليم "مرّة على الأقل": معالج يُعاد بعد نجاح جزئي لا يضرّ (رمز جديد يُبطل القديم؛ إشعار مكرّر نادر ومقبول).
public interface INotificationMessageHandler<in TMessage>
{
    Task HandleAsync(TMessage message, CancellationToken ct);
}

public static class NotificationMessageDispatch
{
    public static async Task DispatchAsync(IServiceProvider services, object message, CancellationToken ct)
    {
        var handlerType = typeof(INotificationMessageHandler<>).MakeGenericType(message.GetType());
        var handler = services.GetService(handlerType)
            ?? throw new InvalidOperationException($"لا معالج لرسالة الصادر {message.GetType().Name}");
        Task pending;
        try
        {
            pending = (Task)handlerType.GetMethod(nameof(INotificationMessageHandler<object>.HandleAsync))!
                .Invoke(handler, [message, ct])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
        await pending;
    }
}

// إعادة المحاولة: تباعد متصاعد يحتمل انقطاع مزوّد لساعات، ثم "رسالة ميتة" تبقى للتشخيص ولا تُعاد تلقائياً.
public static class OutboxRetryPolicy
{
    public const int MaxAttempts = 8;

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromHours(6),
    ];

    // failedAttempts: عدد المحاولات الفاشلة بما فيها هذه. null ⇒ لا محاولة أخرى (ميتة).
    public static DateTime? NextAttemptAt(int failedAttempts, DateTime now)
    {
        if (failedAttempts < 1) throw new ArgumentOutOfRangeException(nameof(failedAttempts));
        return failedAttempts >= MaxAttempts ? null : now + Delays[Math.Min(failedAttempts, Delays.Length) - 1];
    }
}
