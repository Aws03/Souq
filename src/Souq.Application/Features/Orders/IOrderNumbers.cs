namespace Souq.Application.Features.Orders;

// رقم الطلب التالي في متجر السياق (المرحلة 9). يُستدعى داخل معاملة إنشاء الطلب فقط: التنفيذ يقفل صفّ عدّاد المتجر حتى
// الالتزام، فلا يأخذ طلبان متزامنان الرقم نفسه، ورقم معاملة أُلغيت يعود معها.
public interface IOrderNumbers
{
    Task<int> NextAsync(CancellationToken ct);
}
