using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// Review — تقييم عميل لمنتج. لا خاصية تنقّل لـ Customer/Product/Order هنا عمداً
// (نفس نمط Order مع Customer) — التقييم لا يحتاج الكيانات كاملة، فقط معرّفاتها.
// OrderId يُخزَّن كدليل أن التقييم من عميل اشترى المنتج فعلاً واستلمه (تُفرض
// هذه القاعدة في Application عند الإنشاء، لا هنا — الكيان لا يعرف Order إطلاقاً).
//
// الإشراف (المرحلة 13، ADR-0033): التقييم يُنشأ معلّقاً، أو معتمداً إن فعّل المتجر الاعتماد التلقائي. المشرف يعتمد أو يرفض
// — والرفض يخفي تقييماً معتمداً، والاعتماد يعيد مرفوضاً — ويُسجَّل من أشرف ومتى وملاحظته (للإدارة وحدها). تكرار الحالة نفسها
// لا يغيّر شيئاً.
// ============================================================================
public class Review : Entity, ITenantOwned
{
    public const int CommentMaxLength = 1000;
    public const int ModerationNoteMaxLength = 500;

    public int TenantId { get; private set; }
    public int ProductId { get; private set; }
    public int CustomerId { get; private set; }
    public int OrderId { get; private set; }
    public int Rating { get; private set; }
    public string Comment { get; private set; } = default!;
    public ReviewStatus Status { get; private set; }
    public DateTime? ModeratedAt { get; private set; }
    public int? ModeratedByUserId { get; private set; }
    public string? ModerationNote { get; private set; }

    public bool IsPublished => Status == ReviewStatus.Approved;

    private Review() { }

    public Review(int productId, int customerId, int orderId, int rating, string comment, bool approved = false)
    {
        if (rating < 1 || rating > 5)
            throw new InvalidReviewException("التقييم يجب أن يكون بين 1 و5");
        if (string.IsNullOrWhiteSpace(comment))
            throw new InvalidReviewException("نص التقييم مطلوب");
        if (comment.Length > CommentMaxLength)
            throw new InvalidReviewException("نص التقييم طويل جداً (١٠٠٠ حرف كحد أقصى)");

        ProductId = productId;
        CustomerId = customerId;
        OrderId = orderId;
        Rating = rating;
        Comment = comment.Trim();
        Status = approved ? ReviewStatus.Approved : ReviewStatus.Pending;
    }

    public void Approve(int moderatorUserId, DateTime now) => Moderate(ReviewStatus.Approved, moderatorUserId, now, note: null);

    public void Reject(int moderatorUserId, DateTime now, string? note) => Moderate(ReviewStatus.Rejected, moderatorUserId, now, note);

    private void Moderate(ReviewStatus target, int moderatorUserId, DateTime now, string? note)
    {
        if (moderatorUserId <= 0)
            throw new InvalidReviewException("المشرف مطلوب");
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > ModerationNoteMaxLength })
            throw new InvalidReviewException($"ملاحظة الإشراف حتى {ModerationNoteMaxLength} حرف");
        if (Status == target) return;

        Status = target;
        ModeratedAt = now;
        ModeratedByUserId = moderatorUserId;
        ModerationNote = trimmed;
    }
}
