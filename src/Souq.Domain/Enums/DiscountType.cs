namespace Souq.Domain.Enums;

// نوع الخصم الذي يطبّقه الكوبون: نسبة مئوية من الإجمالي، أو مبلغ ثابت.
public enum DiscountType
{
    Percentage = 0,
    FixedAmount = 1
}
