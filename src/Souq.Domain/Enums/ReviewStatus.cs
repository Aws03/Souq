namespace Souq.Domain.Enums;

// حالة تقييم (المرحلة 13): المعلّق ينتظر الإشراف ولا يُعرض، المعتمد يُعرض ويدخل المتوسط، والمرفوض مخفيّ (يُعاد اعتماده).
public enum ReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}
