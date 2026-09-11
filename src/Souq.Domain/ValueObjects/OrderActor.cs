using Souq.Domain.Enums;
using Souq.Domain.Exceptions;

namespace Souq.Domain.ValueObjects;

// من غيّر حالة الطلب (المرحلة 9): يُسجَّل مع كل سطر في سجلّ الحالة — "من فعل ماذا ومتى" بلا الرجوع لسجلّ التدقيق.
public sealed record OrderActor
{
    private OrderActor(OrderActorKind kind, int? userId)
    {
        Kind = kind;
        UserId = userId;
    }

    public OrderActorKind Kind { get; }
    public int? UserId { get; }

    public static readonly OrderActor System = new(OrderActorKind.System, null);
    public static readonly OrderActor PaymentGateway = new(OrderActorKind.PaymentGateway, null);

    // معرّف الحساب اختياري: الطلب يعرف عميله أصلاً (CustomerId).
    public static OrderActor Customer(int? userId = null) => new(OrderActorKind.Customer, userId);

    public static OrderActor Staff(int userId) =>
        userId > 0 ? new(OrderActorKind.Staff, userId) : throw new InvalidOrderOperationException("حساب الموظّف غير صالح");
}
