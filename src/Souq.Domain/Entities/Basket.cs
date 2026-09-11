using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// السلة (المرحلة 8، وحدة Shopping، ADR-0028): نيّة شراء لا سجلّ مالي. مالكها عميل أو زائر (رمز ملف تعريف ارتباط تُحفظ
// بصمته وحدها — تسريب القاعدة لا يكشف سلال الزوّار)، وسطرها متغيّر وكمية فقط: السعر يُقرأ من الكتالوج الحيّ عند كل
// تسعير، والمخزون لا يُحجز هنا (الحجز عند الدفع وحده، ADR-0026). الانتهاء منزلق — كل تعديل يمدّه.
// ============================================================================
public class Basket : Entity, ITenantOwned
{
    public const int MaxLines = 50;
    public const int MaxQuantityPerLine = 99;
    public const int GuestTokenHashLength = 64;   // SHA-256 بالست عشري

    private readonly List<BasketLine> _lines = new();

    private Basket() { }

    private Basket(int? customerId, string? guestTokenHash, DateTime expiresAt)
    {
        CustomerId = customerId;
        GuestTokenHash = guestTokenHash;
        ExpiresAt = expiresAt;
    }

    public int TenantId { get; private set; }
    public int? CustomerId { get; private set; }
    public string? GuestTokenHash { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public IReadOnlyCollection<BasketLine> Lines => _lines.AsReadOnly();

    public bool IsGuest => CustomerId is null;

    public static Basket ForCustomer(int customerId, DateTime expiresAt) =>
        customerId > 0 ? new(customerId, null, expiresAt) : throw new InvalidBasketOperationException("عميل السلة غير صالح");

    public static Basket ForGuest(string guestTokenHash, DateTime expiresAt) =>
        guestTokenHash is { Length: GuestTokenHashLength }
            ? new(null, guestTokenHash, expiresAt)
            : throw new InvalidBasketOperationException("رمز سلة الزائر غير صالح");

    public bool IsExpired(DateTime now) => ExpiresAt <= now;

    public BasketLine? LineFor(int productId) => _lines.FirstOrDefault(l => l.ProductId == productId);

    // إضافة كمية لمتغيّر تُدمج مع سطره إن وُجد. طلب صريح يتجاوز الحدّ يُرفض — لا قصّ صامت لما طلبه العميل.
    public void Add(int productId, int variantId, int quantity, DateTime expiresAt)
    {
        EnsureQuantity(quantity, allowZero: false);
        var line = _lines.FirstOrDefault(l => l.VariantId == variantId);
        if (line is null)
        {
            if (_lines.Count >= MaxLines)
                throw new InvalidBasketOperationException($"السلة تتّسع لـ {MaxLines} صنفاً مختلفاً على الأكثر");
            _lines.Add(new BasketLine(productId, variantId, quantity));
        }
        else
        {
            EnsureQuantity(line.Quantity + quantity, allowZero: false);
            line.SetQuantity(line.Quantity + quantity);
        }
        ExpiresAt = expiresAt;
    }

    // كمية جديدة لسطر قائم؛ الصفر يحذفه.
    public void SetQuantity(int variantId, int quantity, DateTime expiresAt)
    {
        EnsureQuantity(quantity, allowZero: true);
        var line = Line(variantId);
        if (quantity == 0) _lines.Remove(line);
        else line.SetQuantity(quantity);
        ExpiresAt = expiresAt;
    }

    public void Remove(int variantId, DateTime expiresAt)
    {
        _lines.Remove(Line(variantId));
        ExpiresAt = expiresAt;
    }

    public void Clear(DateTime expiresAt)
    {
        _lines.Clear();
        ExpiresAt = expiresAt;
    }

    // دمج سلة الزائر في سلة العميل عند دخوله: تلقائي لا طلب صريح، فلا يفشل لأجله — الكميات تُجمع مقصوصةً لحدّ السطر،
    // وما لا تتّسع له السلة يُهمَل. حذف سلة الزائر بعده مسؤولية المستدعي.
    public void MergeFrom(Basket guest, DateTime expiresAt)
    {
        if (!guest.IsGuest || IsGuest)
            throw new InvalidBasketOperationException("الدمج من سلة زائر إلى سلة عميل فقط");

        foreach (var incoming in guest._lines)
        {
            var line = _lines.FirstOrDefault(l => l.VariantId == incoming.VariantId);
            if (line is not null)
                line.SetQuantity(Math.Min(line.Quantity + incoming.Quantity, MaxQuantityPerLine));
            else if (_lines.Count < MaxLines)
                _lines.Add(new BasketLine(incoming.ProductId, incoming.VariantId, incoming.Quantity));
        }
        ExpiresAt = expiresAt;
    }

    private BasketLine Line(int variantId) =>
        _lines.FirstOrDefault(l => l.VariantId == variantId)
        ?? throw new InvalidBasketOperationException("الصنف ليس في السلة");

    private static void EnsureQuantity(int quantity, bool allowZero)
    {
        if (quantity < (allowZero ? 0 : 1) || quantity > MaxQuantityPerLine)
            throw new InvalidBasketOperationException($"الكمية بين 1 و{MaxQuantityPerLine}");
    }
}

// سطر سلة: المتغيّر (الوحدة القابلة للبيع، D-21) ومنتجه وكميته — بلا سعر.
public class BasketLine : Entity, ITenantOwned
{
    private BasketLine() { }

    internal BasketLine(int productId, int variantId, int quantity)
    {
        ProductId = productId;
        VariantId = variantId;
        Quantity = quantity;
    }

    public int TenantId { get; private set; }
    public int ProductId { get; private set; }
    public int VariantId { get; private set; }
    public int Quantity { get; private set; }

    internal void SetQuantity(int quantity) => Quantity = quantity;
}
