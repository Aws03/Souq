using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Auth.Commands;

public record LoginCommand(string Email, string Password) : IRequest<Result<AuthResponse>>;
