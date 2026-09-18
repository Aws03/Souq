using FluentValidation;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Orders.Commands;

public class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        // عنوان من الدفتر أو عنوان نصّي — أحدهما مطلوب. عنوان الفوترة من الدفتر اختياري (المرحلة 9).
        RuleFor(x => x.ShippingAddress).NotEmpty().MaximumLength(Order.ShippingAddressMaxLength).When(x => x.ShippingAddressId is null);
        RuleFor(x => x.ShippingAddressId).GreaterThan(0).When(x => x.ShippingAddressId is not null);
        RuleFor(x => x.BillingAddressId).GreaterThan(0).When(x => x.BillingAddressId is not null);
        // بلا أسطر ⇒ من سلة العميل (المرحلة 9)؛ الأسطر المُرسَلة بحدّ السلة نفسه.
        RuleFor(x => x.Items!.Count).LessThanOrEqualTo(Basket.MaxLines).When(x => x.Items is not null);
        RuleFor(x => x.CouponCode).MaximumLength(50).When(x => x.CouponCode is not null);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            // السقف هو سقف السلة نفسه (M15): الدفع يقبل `items` صريحة تتجاوز السلة
            // (CheckoutQuote يُفضّلها عليها)، فحدٌّ أدنى وحده كان يعني أنّ نفس القاعدة التي تحرسها
            // السلة بثلاث طبقات — مُحقِّق ومجال وقيد قاعدة — لا تُطبَّق على المسار الذي يُنشئ الطلب.
            // وكميّتان ضخمتان لنفس المتغيّر في طلبٍ واحد تُجمَعان بحسابٍ مُدقَّق، فيصير الفيض 500 مع
            // أثرٍ في السجلّ لكل طلب، على نقطةٍ بلا حدّ معدّل.
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, Basket.MaxQuantityPerLine);
            item.RuleFor(i => i.ProductId).GreaterThan(0);
            item.RuleFor(i => i.VariantId).GreaterThan(0).When(i => i.VariantId is not null);
        });
    }
}
