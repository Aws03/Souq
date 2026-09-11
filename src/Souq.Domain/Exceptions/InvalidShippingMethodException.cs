namespace Souq.Domain.Exceptions;

// قاعدة طريقة شحن أو لقطة شحن مخالَفة (المرحلة 12): اسم، حدّ مجانية، مدّة، رابط تتبّع، دول، أو عملة لا تطابق.
public class InvalidShippingMethodException : DomainException
{
    public InvalidShippingMethodException(string message) : base("InvalidShippingMethod", message) { }
}
