namespace Souq.Application.Common.Interfaces;
public interface IEmailService
{
    Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default);
}
