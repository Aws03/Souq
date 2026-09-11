namespace Souq.Domain.Exceptions;

// خطأ: بيانات عميل أو عنوان غير صالحة، أو عملية على ملف محذوف أو تتجاوز حدّ دفتر العناوين.
public class InvalidCustomerDataException : DomainException
{
    public InvalidCustomerDataException(string message) : base("InvalidCustomerData", message) { }
}
