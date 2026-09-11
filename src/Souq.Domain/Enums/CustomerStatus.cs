namespace Souq.Domain.Enums;

// حالة العميل التجارية داخل متجره (المرحلة 7). المحظور يرى حسابه وطلباته ويصدّر بياناته، لكنه لا يطلب ولا يقيّم.
// إيقاف الدخول نفسه قرار هوية (User.Disable)، منفصل عن الحظر التجاري.
public enum CustomerStatus
{
    Active = 0,
    Blocked = 1,
}
