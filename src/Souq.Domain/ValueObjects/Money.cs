namespace Souq.Domain.ValueObjects;

// ============================================================================
// لماذا "كائن قيمة" (Value Object) للمال؟
// المال ليس مجرد رقم decimal. مبلغ بلا عملة عديم المعنى (100 دولار ≠ 100 دينار).
// نغلّف المبلغ + العملة معاً، ونمنحه قواعده الخاصة (لا مبلغ سالب، جمع بنفس العملة).
//
// الخاصية المميِّزة لكائن القيمة: يُقارَن بمحتواه لا بهويّته.
// مبلغان (100, "JOD") متساويان دائماً — لا يهمّ "أيّهما". لذلك نجعله record
// لأن record في C# يوفّر المساواة بالقيمة تلقائياً.
//
// والأهم: هو "غير قابل للتغيير" (immutable). أي عملية تُنتج كائناً جديداً
// بدل تعديل القديم — هذا يمنع أخطاءً خفية كثيرة.
// ============================================================================
public record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "JOD")
    {
        // قاعدة محمية داخل الكائن نفسه: المبلغ لا يكون سالباً أبداً.
        if (amount < 0)
            throw new ArgumentException("لا يمكن أن يكون المبلغ سالباً", nameof(amount));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("العملة مطلوبة", nameof(currency));

        Amount = amount;
        Currency = currency;
    }

    // الجمع يُعيد كائناً جديداً، ويرفض جمع عملتين مختلفتين (قاعدة عمل محمية).
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("لا يمكن جمع عملتين مختلفتين");
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity) => new(Amount * quantity, Currency);

    // قيمة جاهزة للصفر — تُستخدم كنقطة بداية عند جمع عناصر الطلب.
    public static Money Zero(string currency = "JOD") => new(0, currency);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
