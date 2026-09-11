using Souq.Domain.Exceptions;

namespace Souq.Domain.ValueObjects;

// ============================================================================
// لماذا "كائن قيمة" (Value Object) للمال؟
// المال ليس مجرد رقم decimal. مبلغ بلا عملة عديم المعنى (100 دولار ≠ 100 دينار).
// نغلّف المبلغ + العملة معاً، ونمنحه قواعده الخاصة (لا مبلغ سالب، جمع بنفس العملة).
//
// الخاصية المميِّزة لكائن القيمة: يُقارَن بمحتواه لا بهويّته — record يوفّر ذلك،
// وهو غير قابل للتغيير (immutable): كل عملية تُنتج كائناً جديداً.
//
// قاعدة الدقّة (ADR-0014): المبلغ يجب أن يكون قابلاً للتمثيل بخانات عملته الصغرى
// (الدينار 3 خانات، الدولار 2، الين 0). المُنشئ يرفض ما يتجاوزها، والحسابات التي
// تُنتج كسوراً (نِسب الخصم) تمرّ عبر FromCalculation التي تقرّب في مكان واحد —
// فيتطابق المبلغ في الذاكرة مع المخزَّن في القاعدة مع المُحصَّل من بوّابة الدفع.
// ============================================================================
public record Money
{
    // العملة الافتراضية مؤقتة حتى تحمل كل متجر عملته (المرحلة 2 — تعدّد المستأجرين).
    public const string DefaultCurrency = "JOD";

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        var code = NormalizeCurrency(currency);
        if (amount < 0)
            throw new InvalidMoneyException("لا يمكن أن يكون المبلغ سالباً");

        var minorUnits = CurrencyInfo.MinorUnits(code);
        if (decimal.Round(amount, minorUnits) != amount)
            throw new InvalidMoneyException(
                $"المبلغ {amount} يتجاوز الخانات العشرية المسموحة لعملة {code} ({minorUnits})");

        Amount = amount;
        Currency = code;
    }

    // لنتائج الحساب (نسبة مئوية، ضريبة): تقريب تجاري (0.0005 ⇒ 0.001) إلى خانات
    // العملة الصغرى — المكان الوحيد المسموح فيه بالتقريب.
    public static Money FromCalculation(decimal amount, string currency)
    {
        var code = NormalizeCurrency(currency);
        var rounded = decimal.Round(amount, CurrencyInfo.MinorUnits(code), MidpointRounding.AwayFromZero);
        return new Money(rounded, code);
    }

    // الجمع يُعيد كائناً جديداً، ويرفض جمع عملتين مختلفتين (قاعدة عمل محمية).
    public Money Add(Money other)
    {
        EnsureSameCurrency(other, "لا يمكن جمع عملتين مختلفتين");
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity) => new(Amount * quantity, Currency);

    // الطرح يرث حراسة "لا سالب" من المُنشئ: خصم أكبر من المبلغ يرمي بدل نتيجة سالبة صامتة.
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other, "لا يمكن طرح عملتين مختلفتين");
        return new Money(Amount - other.Amount, Currency);
    }

    // قيمة جاهزة للصفر — تُستخدم كنقطة بداية عند جمع عناصر الطلب.
    public static Money Zero(string currency = DefaultCurrency) => new(0, currency);

    public override string ToString() =>
        $"{Amount.ToString($"F{CurrencyInfo.MinorUnits(Currency)}", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

    private void EnsureSameCurrency(Money other, string message)
    {
        if (Currency != other.Currency)
            throw new InvalidMoneyException(message);
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new InvalidMoneyException("العملة مطلوبة");
        var code = currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            throw new InvalidMoneyException($"رمز العملة غير صالح: {currency}");
        return code;
    }
}
