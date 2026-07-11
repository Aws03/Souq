using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// CreateOrderHandler — "منسّق" حالة الاستخدام الأهم في النظام.
//
// لاحظ دور هذا المعالج: هو لا يحوي قواعد عمل بنفسه (تلك في الكيانات)، بل
// "ينسّق" بين عدة أجزاء لإتمام عملية واحدة. هذا تقسيم مسؤوليات صحيح:
//   - الكيان (Product/Order) = يحرس قواعده.
//   - المعالج (هذا) = يرتّب الخطوات بالترتيب الصحيح.
//
// الخطوات بترتيب مقصود:
//   1) نتحقّق من وجود كل منتج وتوفّر كميته (نفشل مبكراً قبل أي تعديل).
//   2) نبني الطلب وننقص المخزون.
//   3) نُحصّل الدفع عبر الواجهة (لا نعرف Stripe أو PayPal — فقط IPaymentService).
//   4) نحفظ كل شيء ذرّياً عبر وحدة العمل (الكل ينجح أو الكل يُلغى).
// ============================================================================
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderCreatedDto>>
{
    private readonly IProductRepository _products;
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IPaymentService _payment;
    private readonly IEmailService _email;
    private readonly IUnitOfWork _uow;

    public CreateOrderHandler(
        IProductRepository products, IOrderRepository orders, ICustomerRepository customers,
        IPaymentService payment, IEmailService email, IUnitOfWork uow)
    {
        _products = products; _orders = orders; _customers = customers;
        _payment = payment; _email = email; _uow = uow;
    }

    public async Task<Result<OrderCreatedDto>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        // CustomerId يأتي من توكن المستخدم (يفرضه الـ Controller) لا من جسم الطلب،
        // فوجوده مضمون منطقياً؛ نتحقّق دفاعياً ونستخدم بريده الحقيقي في التأكيد.
        var customer = await _customers.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null)
            return Result<OrderCreatedDto>.Failure("العميل غير موجود", "CustomerNotFound");

        var order = new Order(cmd.CustomerId, cmd.ShippingAddress);

        // (1)+(2) نمرّ على كل سطر: نتحقّق ثم نضيف وننقص المخزون.
        foreach (var line in cmd.Items)
        {
            var product = await _products.GetByIdAsync(line.ProductId, ct);
            if (product is null)
                return Result<OrderCreatedDto>.Failure(
                    $"المنتج رقم {line.ProductId} غير موجود", "ProductNotFound");

            try
            {
                // الكيان يحرس القاعدة: يرمي استثناءً إن لم تتوفّر الكمية.
                product.DecreaseStock(line.Quantity);
                // نمرّر لقطة من الاسم والسعر (تُجمّد في الطلب).
                order.AddItem(product.Id, product.Name, product.Price, line.Quantity);
                _products.Update(product);
            }
            catch (InsufficientStockException ex)
            {
                // خطأ متوقّع → نُعيده كـ Result واضح بدل تمرير الاستثناء للأعلى.
                return Result<OrderCreatedDto>.Failure(ex.Message, "InsufficientStock");
            }
        }

        // (3) تحصيل الدفع عبر الواجهة المجرّدة.
        var payResult = await _payment.ChargeAsync(order.TotalAmount, cmd.PaymentToken, ct);
        if (!payResult.Succeeded)
            return Result<OrderCreatedDto>.Failure(
                payResult.FailureReason ?? "فشل الدفع", "PaymentFailed");

        order.MarkAsPaid();                     // الانتقال محروس داخل الكيان
        await _orders.AddAsync(order, ct);

        // (4) حفظ ذرّي: إنقاص المخزون + إضافة الطلب يُحفظان معاً أو لا شيء.
        await _uow.SaveChangesAsync(ct);

        // أثر جانبي غير حرج (البريد): لو فشل لا نُفشل الطلب.
        await _email.SendOrderConfirmationAsync(customer.Email, order.Id, ct);

        return Result<OrderCreatedDto>.Success(new OrderCreatedDto(
            order.Id, order.Status.ToString(),
            order.TotalAmount.Amount, order.TotalAmount.Currency));
    }
}
