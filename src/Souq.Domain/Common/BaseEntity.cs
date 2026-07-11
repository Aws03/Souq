namespace Souq.Domain.Common;

// ============================================================================
// لماذا هذا الملف؟ (مبدأ: DRY - لا تكرّر نفسك)
// كل كيان في النظام يحتاج معرّفاً (Id) وتواريخ إنشاء/تعديل.
// بدل تكرار هذه الحقول في كل كيان، نضعها مرة واحدة في صنف أساسي يرثه الجميع.
// النوع <TId> يجعل نوع المعرّف مرناً (int أو Guid) دون إعادة كتابة الصنف.
// ============================================================================
public abstract class BaseEntity<TId>
{
    public TId Id { get; protected set; } = default!;

    // نسجّل التواريخ تلقائياً (تُملأ في طبقة Infrastructure عبر اعتراض الحفظ في
    // AppDbContext.SaveChangesAsync). الـ setter داخلي لا عام — أي كود خارج طبقة
    // Infrastructure (المُصرَّح لها عبر InternalsVisibleTo في Souq.Domain.csproj)
    // لا يستطيع تزوير هذه التواريخ.
    public DateTime CreatedAt { get; internal set; }
    public DateTime? UpdatedAt { get; internal set; }
}
