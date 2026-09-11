namespace Souq.Domain.Exceptions;

// إشعار داخل التطبيق بلا مستلم، أو بنوع أو بيانات خارج الحدود (المرحلة 14).
public class InvalidNotificationException : DomainException
{
    public InvalidNotificationException(string message) : base("InvalidNotification", message) { }
}
