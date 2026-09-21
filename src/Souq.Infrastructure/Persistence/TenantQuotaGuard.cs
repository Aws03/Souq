using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// حارس الحصص — التنفيذ الوحيد لـ ITenantQuotaGuard (C2، ADR-0049).
//
// **الفكرة كلّها في جملة واحدة:** تحديثٌ مشروط على صفّ عدّاد واحد
//   `UPDATE ... SET Used = Used + 1 WHERE Name = @n AND Used < @limit`
// فإمّا صفٌّ واحد تأثّر (حُجز) أو صفر (بلغ السقف). لا عدَّ ولا قراءة قبل القرار، فلا نافذة بين
// السؤال والجواب أصلاً.
//
// **ولماذا هذا لا يعتمد على مستوى العزل** — وهو الفرق كلّه عن العدّ-ثم-الكتابة (TD-68): التحديث
// يأخذ قفلاً حصرياً، وSQL Server يُقيّم شرط جملة التعديل على **آخر ما التُزم به** لا على لقطة،
// حتى مع READ_COMMITTED_SNAPSHOT — فالمتسابق الثاني ينتظر قفل الأوّل ثم يُعيد تقييم `Used < @limit`
// على القيمة الجديدة فيُرفض. والقارئ الذي يَعُدّ بدل أن يكتب هو من تخدعه اللقطة. ADR-0049 §الخيار أ.
// ولا صنف جمود هنا: المتنافسون يصطفّون على صفٍّ واحد بترتيب واحد، لا على مدى يُقفَل من طرفين.
//
// داخل معاملة المستدعي حصراً: القفل يُحمل حتى الالتزام، فإن فشل إنشاءُ المنتج بعد الحجز تراجَع
// الحجز معه. وهذا يجعله موضع معاملة صريحة جديداً — وهو ما يعني F-14 يوم تُفعَّل إعادة المحاولة
// (ADR-0049 §التبعات).
//
// **يُبقي العدّاد محدَّثاً حتى بلا حدّ**، والبديل مرفوض عن قصد: لو تُرك العدّاد ساكناً ما دام
// المتجر غير مقيَّد، لانحرف عن الحقيقة طوال تلك المدّة، فيوم تُسنَد خطة بحدّ يبدأ المتجر من رقم
// كاذب حتى يمرّ المسح المصالِح — ورقمٌ كاذب هنا يعني سدَّ متجرٍ يدفع. الثمن كتابة صفٍّ واحد على
// مسارين إداريّين نادرين (إنشاء منتج، دعوة موظّف)، لا على أي مسار متسوّق.
// ============================================================================
internal sealed class TenantQuotaGuard : ITenantQuotaGuard
{
    // صفر: لا صفّ ⇒ يُنشأ ويُعاد. واحد: يُحجز عليه. والثالثة سماحٌ لسباق إنشاءين، لا أكثر —
    // حلقة بلا سقف على مسار مقفول هي كيف يصير عطبٌ صغير توقّفاً كاملاً.
    private const int MaxAttempts = 3;

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public TenantQuotaGuard(AppDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    public async Task<QuotaDecision> ReserveAsync(string limitName, CancellationToken ct = default)
    {
        var name = LimitNames.Normalize(limitName);
        var limit = _tenant.RequireTenant().LimitFor(name);

        // خطأ برمجي لا حالة تشغيل: حجزٌ خارج معاملة لا يتراجع مع ما يفشل بعده، فيترك حصّة
        // مستهلَكة على شيء لم يوجد. صاخبٌ عمداً — الصامت منه ينحرف بالعدّاد بلا أثر.
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                $"ReserveAsync('{name}') خارج معاملة: الحجز يجب أن يتراجع مع الإنشاء الذي حجز له.");

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var counters = _db.TenantUsageCounters.Where(c => c.Name == name);

            var taken = limit is { } cap
                ? await counters.Where(c => c.Used < cap)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Used, c => c.Used + 1), ct)
                : await counters
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Used, c => c.Used + 1), ct);

            if (taken == 1)
            {
                var used = await counters.AsNoTracking().Select(c => c.Used).SingleAsync(ct);
                return limit is { } granted
                    ? QuotaDecision.Granted(name, granted, used)
                    : QuotaDecision.Uncapped(name, used);
            }

            // صفر صفّاً: إمّا الصفّ موجود وبلغ سقفه، وإمّا لا صفّ لهذا المتجر بعد.
            if (await counters.AsNoTracking().Select(c => (int?)c.Used).FirstOrDefaultAsync(ct) is { } atCap)
                return QuotaDecision.Denied(name, limit!.Value, atCap);

            // أوّل استعمال لهذا الحدّ في هذا المتجر: الصفّ يُنشأ بالعدد **الفعلي** لا بصفر.
            var counter = TenantUsageCounter.StartAt(name, await QuotaResources.CountAsync(_db, name, ct));
            _db.TenantUsageCounters.Add(counter);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintViolationException)
            {
                // طلب متزامن في المتجر نفسه أنشأ الصفّ أوّلاً — نحجز على صفّه في الدورة التالية.
                // النوع مقصود: AppDbContext يترجم 2601/2627 إليه، وهو ليس DbUpdateException.
                _db.Entry(counter).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException($"تعذّر حجز حصّة '{name}' بعد {MaxAttempts} محاولات.");
    }

    public async Task ReleaseAsync(string limitName, int count = 1, CancellationToken ct = default)
    {
        var name = LimitNames.Normalize(limitName);
        if (count <= 0) return;

        // لا صفّ ⇒ صفر صفّاً متأثّراً ولا شيء يُفعل: أوّل حجزٍ لاحق يُنشئه من العدّ الحقيقي أصلاً.
        // والتثبيت عند الصفر داخل الجملة نفسها لا بقراءةٍ قبلها: عدّاد سالب يجعل الحدّ يُتجاوَز.
        await _db.TenantUsageCounters
            .Where(c => c.Name == name)
            .ExecuteUpdateAsync(s => s.SetProperty(
                c => c.Used, c => c.Used >= count ? c.Used - count : 0), ct);
    }

    public async Task<QuotaDecision> PeekAsync(string limitName, CancellationToken ct = default)
    {
        var name = LimitNames.Normalize(limitName);
        var limit = _tenant.RequireTenant().LimitFor(name);

        // **لا يكتب ولا يقفل** (ADR-0049 §التبعات): صفحةٌ تعرض المتبقّي يجب ألّا تستهلكه. ولا صفّ
        // بعد ⇒ تُعَدّ الحقيقة، فالمعروض صحيح قبل أوّل حجز لا صفر مضلّل.
        var used = await _db.TenantUsageCounters.AsNoTracking()
            .Where(c => c.Name == name).Select(c => (int?)c.Used).FirstOrDefaultAsync(ct)
            ?? await QuotaResources.CountAsync(_db, name, ct);

        if (limit is not { } cap) return QuotaDecision.Uncapped(name, used);
        return used < cap ? QuotaDecision.Granted(name, cap, used) : QuotaDecision.Denied(name, cap, used);
    }

    // ============================================================================
    // المصالحة — **القفل أوّلاً ثم العدّ ثم التصحيح**، وهذا الترتيب هو كل صوابها.
    //
    // لو عُدَّ أوّلاً ثم صُحِّح، لكان التصحيح نفسه عدّاً-ثم-كتابة — أي الخطأ الذي وُجد هذا الصنف
    // كلّه لتجنّبه: حجزٌ يقع بين العدّ والكتابة يُمحى، فيتجاوز المتجر حدّه بواحد بهدوء.
    //
    // والقفل يُؤخذ بتحديثٍ لا يغيّر شيئاً (`Used = Used`). يبدو عبثاً وليس كذلك: SQL Server يأخذ
    // قفلاً حصرياً على الصفّ لأي UPDATE ويحمله حتى الالتزام، وهو ما يجعل كل حجزٍ متزامن ينتظر.
    // وبما أن الحجز **يسبق** الإنشاء دائماً (عقد ReserveAsync)، فلا صفٌّ جديد يُولد ونحن عادّون —
    // فالعدّ تحت القفل يرى عالماً ثابتاً. والبديل (جملة SQL خام بـ UPDLOCK) تمنعه القاعدة
    // المعمارية، وهي محقّة: لا مرشّح مستأجر على SQL الخام.
    // ============================================================================
    public async Task<int> ReconcileAsync(CancellationToken ct = default)
    {
        _tenant.RequireTenant();   // خارج نطاق متجر لا معنى للمصالحة — ولا للعدّ أصلاً.
        var corrected = 0;

        foreach (var name in LimitNames.All)
        {
            corrected += await _db.InTransactionAsync(async () =>
            {
                var counters = _db.TenantUsageCounters.Where(c => c.Name == name);

                // لا صفّ ⇒ لا انحراف: أوّل حجزٍ لاحق يُنشئه من العدّ الحقيقي. ولا يُنشأ هنا عمداً،
                // وإلّا امتلأ الجدول بصفوفٍ لحدودٍ لم يستعملها المتجر قطّ.
                if (await counters.ExecuteUpdateAsync(s => s.SetProperty(c => c.Used, c => c.Used), ct) == 0)
                    return 0;

                var actual = await QuotaResources.CountAsync(_db, name, ct);
                return await counters.Where(c => c.Used != actual)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Used, actual), ct);
            }, ct);
        }

        return corrected;
    }
}
