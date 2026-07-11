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
// الخطوات بترتيب مقصود (نسخة مُقوّاة — مرحلة 4، يعالج بند AUDIT ٧ "الدفع قبل
// الحفظ"): كان الترتيب القديم يُحصّل الدفع ثم يحفظ، فإن فشل الحفظ بعد نجاح
// التحصيل ينتج "عميل دُفع منه بلا طلب محفوظ". الترتيب الجديد:
//   1) نتحقّق من وجود كل منتج وتوفّر كميته وننقص المخزون (نفشل مبكراً قبل أي دفع).
//   2) نحفظ الطلب Pending فوراً — قبل أي محاولة تحصيل. أي محاولة دفع لاحقة
//      ستقابلها دائماً سجل طلب موجود بالفعل.
//   3) نُحصّل الدفع. لو فشل: نُعيد المخزون ونُلغي الطلب (تعويض حجز فاشل) بدل
//      ترك مخزون منقوص بلا مقابل — لا نرمي الاستثناء للأعلى، الفشل هنا متوقّع.
//   4) لو نجح: نُعلّم الطلب مدفوعاً ونحفظ. فشل هذا الحفظ الأخير تحديداً (عطل
//      قاعدة بيانات لحظي بعد نجاح تحصيل حقيقي) يبقى ممكناً نظرياً، لكن الطلب
//      يبقى في القاعدة Pending وقابلاً للتوفيق يدوياً بمطابقة مرجع الدفع —
//      أفضل بكثير من عدم وجود أي سجل إطلاقاً.
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
        var reservedProducts = new List<Product>();   // نحتفظ بها لإعادة المخزون إن فشل الدفع

        // (1) نمرّ على كل سطر: نتحقّق ثم نضيف وننقص المخزون.
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
                reservedProducts.Add(product);
            }
            catch (InsufficientStockException ex)
            {
                // خطأ متوقّع → نُعيده كـ Result واضح بدل تمرير الاستثناء للأعلى.
                return Result<OrderCreatedDto>.Failure(ex.Message, "InsufficientStock");
            }
        }

        // (2) نحفظ الطلب Pending فوراً — قبل أي محاولة تحصيل (انظر التعليق أعلاه).
        await _orders.AddAsync(order, ct);
        await _uow.SaveChangesAsync(ct);

        // (3) تحصيل الدفع عبر الواجهة المجرّدة.
        var payResult = await _payment.ChargeAsync(order.TotalAmount, cmd.PaymentToken, ct);
        if (!payResult.Succeeded)
        {
            // تعويض الحجز الفاشل: نُعيد المخزون ونُلغي الطلب بدل تركه Pending للأبد.
            foreach (var product in reservedProducts)
            {
                var line = order.Items.First(i => i.ProductId == product.Id);
                product.IncreaseStock(line.Quantity);
                _products.Update(product);
            }
            order.Cancel();
            await _uow.SaveChangesAsync(ct);

            return Result<OrderCreatedDto>.Failure(
                payResult.FailureReason ?? "فشل الدفع", "PaymentFailed");
        }

        // (4) نجح الدفع: نُعلّم الطلب مدفوعاً ونحفظ مرة أخرى.
        order.MarkAsPaid();                     // الانتقال محروس داخل الكيان
        await _uow.SaveChangesAsync(ct);

        // أثر جانبي غير حرج (البريد): لو فشل لا نُفشل الطلب.
        await _email.SendOrderConfirmationAsync(customer.Email, order.Id, ct);

        return Result<OrderCreatedDto>.Success(new OrderCreatedDto(
            order.Id, order.Status.ToString(),
            order.TotalAmount.Amount, order.TotalAmount.Currency));
    }
}
