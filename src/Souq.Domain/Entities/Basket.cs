using Souq.Domain.Common;

namespace Souq.Domain.Entities;

// ============================================================================
// Basket — السلة. مجال منفصل عن الطلب (Bounded Context مختلف).
// السلة مؤقتة وقابلة للتغيير الحر، بينما الطلب نهائي ومجمّد. لذلك هما كيانان
// مختلفان رغم تشابههما الظاهري — درس من قسم "تحليل المجالات" في الملف.
// ============================================================================
public class Basket : Entity
{
    private readonly List<BasketItem> _items = new();
    public int CustomerId { get; private set; }
    public IReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();

    private Basket() { }
    public Basket(int customerId) => CustomerId = customerId;

    public void AddItem(int productId, int quantity)
    {
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null) existing.SetQuantity(existing.Quantity + quantity);
        else _items.Add(new BasketItem(productId, quantity));
    }

    public void RemoveItem(int productId) => _items.RemoveAll(i => i.ProductId == productId);
    public void Clear() => _items.Clear();
}

public class BasketItem : Entity
{
    public int ProductId { get; private set; }
    public int Quantity { get; private set; }
    private BasketItem() { }
    internal BasketItem(int productId, int quantity) { ProductId = productId; Quantity = quantity; }
    internal void SetQuantity(int q) => Quantity = q;
}
