namespace Souq.Application.Features.Orders;

// مرجع حجوزات طلب لدى وحدة Inventory: Ordering تملك صيغته، وInventory تحفظه نصّاً ولا تفسّره (Modules.md).
public static class OrderStockReference
{
    private const string Prefix = "order:";

    public static string For(int orderId) => $"{Prefix}{orderId}";

    public static bool TryParse(string reference, out int orderId)
    {
        orderId = 0;
        return reference.StartsWith(Prefix, StringComparison.Ordinal)
               && int.TryParse(reference.AsSpan(Prefix.Length), out orderId) && orderId > 0;
    }
}
