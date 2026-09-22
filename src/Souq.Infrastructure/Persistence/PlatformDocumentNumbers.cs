using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Billing;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// سلسلةُ ترقيم مستندات المنصّة (C5، [ADR-0056](0056)) — نظيرُ `OrderNumbers` بنفس الآلية ونفس
// المزالق، وقد كُتبت من جديد لا شُورِكت لأنّ نطاقهما مختلف: تلك مُرشَّحةٌ بالمتجر، وهذه عالمية
// فتحمل شرطَ سلسلتها بيدها.
//
// **زيادةٌ ذرّية بجملةٍ واحدة** (`ExecuteUpdate`) ثم قراءةُ الرقم في المعاملة نفسها. القفلُ الذي
// يأخذه التحديثُ يبقى حتى الالتزام، فإصدارٌ متزامن ينتظر لحظةً ويأخذ الرقمَ التالي. وتفكيكُها إلى
// «اقرأ ثمّ اكتب» يُعيد بالضبط السباقَ الذي وُجدت لتمنعه — وثمنُه هنا **رقمان متطابقان لفاتورتين**.
//
// **وأوّلُ مستندٍ لسلسلةٍ يُنشئ صفَّها؛ سباقُ إنشاءَين يحسمه الفهرسُ الفريد ثمّ إعادة.** والنوعُ
// المُصطاد `UniqueConstraintViolationException` لا `DbUpdateException`: هذا بعينه العطبُ الذي
// وقع في `OrderNumbers` — `AppDbContext` يترجم 2601/2627 إلى نوعٍ يرث `Exception` مباشرةً، فاصطيادُ
// `DbUpdateException` لا يقع أبداً وأوّلُ متزامنَين يخرج أحدهما بـ 409.
// ============================================================================
internal sealed class PlatformDocumentNumbers : IPlatformDocumentNumbers
{
    private readonly AppDbContext _db;

    public PlatformDocumentNumbers(AppDbContext db) => _db = db;

    public async Task<string> NextAsync(string series, string prefix, CancellationToken ct)
    {
        // **داخل معاملةٍ حصراً.** خارجها يُلتزَم الرقمُ فوراً، فمستندٌ يفشل إصدارُه بعد سحبه يترك
        // فجوةً في سلسلةٍ يُفترض أنّها متّصلة — وهي أوّلُ ما يسأل عنه مراجع.
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Platform document numbers are issued inside the document's transaction only.");

        for (var attempt = 0; ; attempt++)
        {
            var incremented = await _db.PlatformDocumentSequences
                .Where(s => s.Series == series)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastNumber, x => x.LastNumber + 1), ct);

            if (incremented == 1)
            {
                var number = await _db.PlatformDocumentSequences.AsNoTracking()
                    .Where(s => s.Series == series).Select(s => s.LastNumber).SingleAsync(ct);
                return Format(prefix, number);
            }

            var first = PlatformDocumentSequence.Start(series);
            _db.PlatformDocumentSequences.Add(first);
            try
            {
                await _db.SaveChangesAsync(ct);
                return Format(prefix, first.LastNumber);
            }
            catch (UniqueConstraintViolationException) when (attempt == 0)
            {
                // إصدارٌ متزامن أنشأ صفَّ السلسلة أولاً: نزيد صفَّه في الدورة التالية.
                _db.Entry(first).State = EntityState.Detached;
            }
        }
    }

    // `INV000042`: بادئةٌ من إعداد المنصّة وستُّ خاناتٍ بأصفارٍ أمامية. الأصفارُ تجعل الأرقام
    // تُرتَّب نصّياً كما تُرتَّب عدديّاً — وهو ما يحتاجه أيُّ تصديرٍ إلى جدولٍ محاسبيّ. وتجاوزُ
    // ستّ خانات يُطيل الرقمَ ولا يكسره.
    private static string Format(string prefix, int number) =>
        $"{prefix}{number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)}";
}
