namespace Souq.Domain.Exceptions;

// خطأ: عملية مخزون تكسر قاعدة (كمية غير موجبة، تصحيح بلا سبب، إنزال الموجود تحت المحجوز لطلبات قائمة).
public class InvalidInventoryOperationException : DomainException
{
    public InvalidInventoryOperationException(string message) : base("InvalidInventoryOperation", message) { }
}
