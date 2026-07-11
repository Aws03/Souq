using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// Review — تقييم عميل لمنتج. لا خاصية تنقّل لـ Customer/Product/Order هنا عمداً
// (نفس نمط Order مع Customer) — التقييم لا يحتاج الكيانات كاملة، فقط معرّفاتها.
// OrderId يُخزَّن كدليل أن التقييم من عميل اشترى المنتج فعلاً واستلمه (تُفرض
// هذه القاعدة في Application عند الإنشاء، لا هنا — الكيان لا يعرف Order إطلاقاً).
// ============================================================================
public class Review : Entity
{
    public int ProductId { get; private set; }
    public int CustomerId { get; private set; }
    public int OrderId { get; private set; }
    public int Rating { get; private set; }
    public string Comment { get; private set; } = default!;

    private Review() { }

    public Review(int productId, int customerId, int orderId, int rating, string comment)
    {
        if (rating < 1 || rating > 5)
            throw new InvalidReviewException("التقييم يجب أن يكون بين 1 و5");
        if (string.IsNullOrWhiteSpace(comment))
            throw new InvalidReviewException("نص التقييم مطلوب");
        if (comment.Length > 1000)
            throw new InvalidReviewException("نص التقييم طويل جداً (١٠٠٠ حرف كحد أقصى)");

        ProductId = productId;
        CustomerId = customerId;
        OrderId = orderId;
        Rating = rating;
        Comment = comment.Trim();
    }
}
