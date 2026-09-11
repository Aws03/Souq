using Microsoft.EntityFrameworkCore;
using Souq.Application.Features.Orders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// عدّاد أرقام الطلبات (المرحلة 9، ADR-0029): زيادة ذرّية بتحديث واحد (ExecuteUpdate، مُرشَّح بالمتجر كأي استعلام) ثم
// قراءة الرقم في المعاملة نفسها. قفل الصفّ الذي أخذه التحديث يبقى حتى الالتزام، فالطلب المتزامن في المتجر نفسه ينتظر
// لحظة ويأخذ الرقم التالي، ومتجر آخر لا يتأثّر. أول طلب لمتجر يُنشئ صفّه؛ سباق إنشاءين يحسمه الفهرس الفريد ثم إعادة.
// ============================================================================
internal sealed class OrderNumbers : IOrderNumbers
{
    private readonly AppDbContext _db;

    public OrderNumbers(AppDbContext db) => _db = db;

    public async Task<int> NextAsync(CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Order numbers are issued inside the order's transaction only.");

        for (var attempt = 0; ; attempt++)
        {
            var incremented = await _db.OrderNumberSequences
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastNumber, x => x.LastNumber + 1), ct);
            if (incremented == 1)
                return await _db.OrderNumberSequences.AsNoTracking().Select(s => s.LastNumber).SingleAsync(ct);

            var first = OrderNumberSequence.Start();
            _db.OrderNumberSequences.Add(first);
            try
            {
                await _db.SaveChangesAsync(ct);
                return first.LastNumber;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // طلب متزامن في المتجر نفسه أنشأ الصفّ أولاً: نزيد صفّه في الدورة التالية.
                _db.Entry(first).State = EntityState.Detached;
            }
        }
    }
}
