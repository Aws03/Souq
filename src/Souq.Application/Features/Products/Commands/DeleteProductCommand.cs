using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Commands;

// "أمر" حذف منتج. الحذف هنا منطقي (Soft Delete) لا فعلي — انظر المعالج.
// لا يحمل سوى المعرّف، ويُرجع Result (نجح/فشل) فقط.
public record DeleteProductCommand(int Id) : IRequest<Result>;
