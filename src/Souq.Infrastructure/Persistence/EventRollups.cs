using Microsoft.EntityFrameworkCore;
using Souq.Application.Features.Analytics;
using Souq.Application.Features.Analytics.Contracts;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// تجميعُ يومٍ من الصفوف الخام إلى مجاميعَ **بلا معرّف شخص** ([ADR-0050](0050) §6).
//
// وهي التجميعات التي **لا تُحتسب مرّةً ثانية**: ما يربط منتجَين هو جلسةٌ أو طلب، وكلاهما في الصفوف
// الخام التي ستُمسح بانقضاء مدّة الحفظ. فالتجميع يسبق المسح، وعلامةُ `AnalyticsRollupState` هي ما
// يجعل ذلك شرطاً في الكود لا نصيحةً.
//
// **إعادةُ تشغيل يومٍ تُنتج الرقم نفسه**: صفوفُ ذلك اليوم تُستبدَل لا تُجمَع فوق نفسها. وهذا ما
// يجعل الوظيفة قابلةً لإعادة التشغيل بعد انقطاع بلا خوفٍ من عدٍّ مضاعف.
//
// الأحداثُ تُقرأ بحمولاتها ويُفكّ ترميزُها في الذاكرة: المجاميع تحتاج حقولاً **داخل** JSON
// (المنتج، الكمّية، موضعه في القائمة)، وSQL Server يستطيع قراءتها لكنّ ذلك يربط التجميع بشكل
// الحمولة في الاستعلام — وشكلُ الحمولة مُصدَّر ويتغيّر. القراءةُ بالنوع تجعل المُصرِّف يُمسك ذلك.
// ============================================================================
internal sealed class EventRollups : IEventRollups
{
    // سقفُ صفوف اليوم الواحد التي تُقرأ في الذاكرة. يومٌ أكبر من هذا يُجمَّع على ما قُرئ ويُسجَّل
    // نقصُه — والبديل (قراءةٌ بلا حدّ) يُسقط العملية كلّها على متجرٍ ذي ذروة.
    private const int MaxRowsPerDay = 200_000;

    private readonly AppDbContext _db;

    public EventRollups(AppDbContext db) => _db = db;

    public async Task<DateTime?> NextDayToRollUpAsync(DateTime utcNow, CancellationToken ct)
    {
        var state = await _db.AnalyticsRollupStates.FirstOrDefaultAsync(ct);
        var after = state?.RolledUpThroughDay;

        // اليومُ الجاري لا يُجمَّع: أحداثُه لم تنتهِ، وتجميعُه يكتب رقماً ناقصاً ثم يمنع تصحيحَه
        // لأنّ العلامة تتقدّم. آخرُ يومٍ مكتمل هو أمس بـ UTC.
        var lastComplete = utcNow.Date.AddDays(-1);

        var query = _db.BehaviouralEvents.AsNoTracking().Where(e => e.OccurredAt < utcNow.Date);
        if (after is { } watermark) query = query.Where(e => e.OccurredAt >= watermark.AddDays(1));

        var oldest = await query.OrderBy(e => e.OccurredAt).Select(e => (DateTime?)e.OccurredAt).FirstOrDefaultAsync(ct);
        if (oldest is null) return null;

        var day = oldest.Value.Date;
        return day <= lastComplete ? day : null;
    }

    public async Task<DateTime?> RolledUpThroughAsync(CancellationToken ct) =>
        (await _db.AnalyticsRollupStates.AsNoTracking().FirstOrDefaultAsync(ct))?.RolledUpThroughDay;

    public async Task<int> RollUpAsync(DateTime day, DateTime utcNow, CancellationToken ct)
    {
        var from = day.Date;
        var to = from.AddDays(1);

        var rows = await _db.BehaviouralEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt < to)
            .OrderBy(e => e.Id)
            .Take(MaxRowsPerDay)
            .Select(e => new { e.Name, e.Payload, e.SessionId })
            .ToListAsync(ct);

        var engagement = new Dictionary<int, Counts>();
        // مجموعاتُ التجاور: عناصرُ جلسةٍ واحدة (رُئيت معاً) وعناصرُ طلبٍ واحد (اشتُريت معاً).
        var viewedPerSession = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var purchasedPerOrder = new Dictionary<int, HashSet<int>>();

        foreach (var row in rows)
        {
            var payload = BehaviouralEventPayloads.Deserialize(row.Name, row.Payload);
            if (payload is null) continue;   // حمولةٌ لا يعرفها هذا الإصدار تُتجاهَل، لا تُسقط اليوم.

            switch (row.Name, payload)
            {
                case (BehaviouralEventNames.ListViewed, ListViewedPayload list):
                    foreach (var item in list.Items) Count(engagement, item.ProductId).Impressions++;
                    break;

                case (BehaviouralEventNames.ListClicked, ListClickedPayload click):
                    Count(engagement, click.ProductId).Clicks++;
                    break;

                case (BehaviouralEventNames.ItemViewed, ItemViewedPayload view):
                    // التجاورُ في العرض يُقاس بصفحات المنتجات التي زارها الزائر في جلسةٍ واحدة —
                    // لا بظهورٍ في قائمة: كلُّ عنصرَين في صفحةِ نتائجٍ واحدة "رُئيا معاً" بلا معنى.
                    if (row.SessionId is { } session) Group(viewedPerSession, session).Add(view.ProductId);
                    break;

                case (BehaviouralEventNames.CartAdded, CartChangedPayload add):
                    Count(engagement, add.ProductId).CartAdds++;
                    break;

                case (BehaviouralEventNames.OrderPlaced, OrderPlacedPayload order):
                    var items = order.Items.Select(i => i.ProductId).Distinct().Take(ProductPairDaily.MaxItemsPerGroup);
                    foreach (var line in order.Items)
                    {
                        var counts = Count(engagement, line.ProductId);
                        counts.Purchases++;
                        counts.UnitsSold += line.Quantity;
                    }
                    purchasedPerOrder[order.OrderId] = [.. items];
                    break;
            }
        }

        var written = await ReplaceEngagementAsync(from, engagement, ct);
        written += await ReplacePairsAsync(from, viewedPerSession.Values, purchasedPerOrder.Values, ct);

        var state = await _db.AnalyticsRollupStates.FirstOrDefaultAsync(ct);
        if (state is null)
        {
            state = AnalyticsRollupState.Empty();
            _db.AnalyticsRollupStates.Add(state);
        }
        state.Advance(from, utcNow);

        await _db.SaveChangesAsync(ct);
        return written;
    }

    private async Task<int> ReplaceEngagementAsync(DateTime day, Dictionary<int, Counts> counts, CancellationToken ct)
    {
        var existing = await _db.ProductEngagementDailies.Where(r => r.Day == day).ToListAsync(ct);
        var byProduct = existing.ToDictionary(r => r.ProductId);

        foreach (var (productId, c) in counts)
        {
            if (byProduct.TryGetValue(productId, out var row))
                row.Replace(c.Impressions, c.Clicks, c.CartAdds, c.Purchases, c.UnitsSold);
            else
                _db.ProductEngagementDailies.Add(ProductEngagementDaily.For(
                    day, productId, c.Impressions, c.Clicks, c.CartAdds, c.Purchases, c.UnitsSold));
        }

        // صفٌّ لليوم لم يعد له مقابل في الاحتساب الجديد يُصفَّر لا يُحذف: الحذف كتابةٌ مجمّعة، وهذا
        // العدد صغير بطبعه (منتجاتُ يومٍ واحد).
        foreach (var orphan in existing.Where(r => !counts.ContainsKey(r.ProductId)))
            orphan.Replace(0, 0, 0, 0, 0);

        return counts.Count;
    }

    private async Task<int> ReplacePairsAsync(
        DateTime day, IEnumerable<HashSet<int>> viewGroups, IEnumerable<HashSet<int>> purchaseGroups, CancellationToken ct)
    {
        var pairs = new Dictionary<(int Low, int High), (int CoViews, int CoPurchases)>();

        void Add(IEnumerable<HashSet<int>> groups, bool purchased)
        {
            foreach (var group in groups)
            {
                var items = group.Take(ProductPairDaily.MaxItemsPerGroup).OrderBy(id => id).ToList();
                for (var i = 0; i < items.Count; i++)
                for (var j = i + 1; j < items.Count; j++)
                {
                    var key = (Low: items[i], High: items[j]);
                    var current = pairs.TryGetValue(key, out var found) ? found : (CoViews: 0, CoPurchases: 0);
                    pairs[key] = purchased
                        ? (current.CoViews, current.CoPurchases + 1)
                        : (current.CoViews + 1, current.CoPurchases);
                }
            }
        }

        Add(viewGroups, purchased: false);
        Add(purchaseGroups, purchased: true);

        var existing = await _db.ProductPairDailies.Where(r => r.Day == day).ToListAsync(ct);
        var byPair = existing.ToDictionary(r => (r.ProductIdLow, r.ProductIdHigh));

        foreach (var (key, value) in pairs)
        {
            if (byPair.TryGetValue(key, out var row)) row.Replace(value.CoViews, value.CoPurchases);
            else if (ProductPairDaily.For(day, key.Low, key.High, value.CoViews, value.CoPurchases) is { } created)
                _db.ProductPairDailies.Add(created);
        }

        foreach (var orphan in existing.Where(r => !pairs.ContainsKey((r.ProductIdLow, r.ProductIdHigh))))
            orphan.Replace(0, 0);

        return pairs.Count;
    }

    private static Counts Count(Dictionary<int, Counts> map, int productId) =>
        map.TryGetValue(productId, out var found) ? found : map[productId] = new Counts();

    private static HashSet<int> Group(Dictionary<string, HashSet<int>> map, string key) =>
        map.TryGetValue(key, out var found) ? found : map[key] = [];

    private sealed class Counts
    {
        public int Impressions;
        public int Clicks;
        public int CartAdds;
        public int Purchases;
        public int UnitsSold;
    }
}

// ============================================================================
// مسحُ الصفوف الخام. مفتاحٌ بدفعات كما في `SearchLogRetention`، ولنفس السبب: `DELETE TOP` لا
// يُعبَّر عنه في LINQ، والعزل من مرشّح المستأجر وحده — الكيان `ITenantOwned` فالجملة مُرشَّحة.
// ============================================================================
internal sealed class EventStoreRetention : IEventStoreRetention
{
    private readonly AppDbContext _db;

    public EventStoreRetention(AppDbContext db) => _db = db;

    public async Task<int> PurgeBeforeAsync(DateTime before, int max, CancellationToken ct)
    {
        var ids = await _db.BehaviouralEvents.AsNoTracking()
            .Where(e => e.OccurredAt < before)
            .OrderBy(e => e.OccurredAt)
            .Select(e => e.Id)
            .Take(max)
            .ToListAsync(ct);

        if (ids.Count == 0) return 0;

        return await _db.BehaviouralEvents.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(ct);
    }
}
