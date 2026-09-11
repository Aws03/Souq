namespace Souq.Domain.Exceptions;

// خطأ: عملية سلة تخالف قاعدتها — كمية خارج حدّها، صنف ليس في السلة، أو سلة ممتلئة.
public class InvalidBasketOperationException : DomainException
{
    public InvalidBasketOperationException(string message) : base("InvalidBasketOperation", message) { }
}
