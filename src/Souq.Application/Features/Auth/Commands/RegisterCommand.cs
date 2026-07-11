using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Auth.Commands;

// تسجيل حساب عميل جديد. يُعيد توكناً جاهزاً (دخول تلقائي بعد التسجيل).
public record RegisterCommand(string FullName, string Email, string Password)
    : IRequest<Result<AuthResponse>>;
