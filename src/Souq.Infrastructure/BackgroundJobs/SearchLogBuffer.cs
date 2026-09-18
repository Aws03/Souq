using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Contracts;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// ذاكرة السجلّ المحدودة (M13) — هي كلّ ما يلمسه مسار البحث.
//
// **لماذا ذاكرة وكاتبٌ خلفي، لا كتابةٌ مباشرة ولا صندوق صادر؟**
//   • الكتابة المباشرة تضع `INSERT` في مسار كلّ بحث: سريعةٌ عادةً، وفي أسوأ يومٍ هي ما يُبطئ البحث
//     نفسه. وقياسُ شيءٍ لا يجوز أن يُبطئه.
//   • "أطلق وانسَ" بـ `Task.Run` تفقد نطاق الخدمات وتُخفي الاستثناء، فيصير الفقد صامتاً.
//   • صندوق الصادر جدولٌ دائم لرسائل **يجب** أن تُسلَّم؛ سجلّ البحث إحصاءٌ يُقبل فقدُ طرفه.
// فالحلّ هو الوسط: قناة محدودة في الذاكرة، وكاتبٌ يُفرغها على دفعات داخل نطاق كلّ متجر.
//
// **والحدّ يُسقط ولا ينتظر** (`DropWrite`): امتلاءُ القناة يعني أنّ الكاتب لا يلحق، وحينها الاختيار بين
// إسقاط قياسٍ أو تعليق بحثٍ — والأول هو الجواب. والمُسقَط يُعَدّ ويُسجَّل تحذيراً، فلا يكون صامتاً.
//
// **ومعرّف المتجر يُلتقط عند الإيداع، بجانب الصفّ لا داخله**: الكيانات لا تملك setter لـ `TenantId`
// أصلاً — يختمه `TenantWriteGuardInterceptor` من سياق المستأجر عند الحفظ. فالمعرّف يُحمل في الذاكرة
// إلى جانب الصفّ، والكاتب يجمع الأسطر بمتجرها ويحفظ كلّ مجموعة **داخل نطاق متجرها**، فيختم الحارس
// الصحيح. ولو أُجِّل الالتقاط إلى لحظة الكتابة لما كان لدى الكاتب سياقٌ يُختم منه.
//
// **والقناة مفردة والمُسجِّل بنطاق، وهما صنفان لهذا السبب وحده.** القناة يتشاركها الكاتب الخلفي مع كلّ
// طلب، فعمرها عمر التطبيق؛ والمُسجِّل يقرأ `ITenantContext` وهي بنطاق الطلب. دمجُهما في صنف واحد يعني
// خدمةً بنطاق تُطلب من مزوّدٍ جذري — وهو بالضبط ما منع الـ API من الإقلاع في Development حتى M11.
// ============================================================================

/// القناة وحدها: مفردة، بلا أيّ تبعية بنطاق.
internal sealed class SearchLogChannel
{
    // ألفٌ من الأسطر: أكثر ممّا يُنتجه بحثٌ طبيعي بين دفعتين بمراتب، وأقلّ ممّا يُقلق ذاكرةً بمراتب
    // (السطر بضع عشرات من البايتات). الحدّ موجود ليكون موجوداً، لا ليُبلَغ.
    public const int Capacity = 1000;

    private readonly Channel<BufferedSearch> _channel = Channel.CreateBounded<BufferedSearch>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<BufferedSearch> Reader => _channel.Reader;

    public bool TryWrite(BufferedSearch entry) => _channel.Writer.TryWrite(entry);
}

/// المُسجِّل: بنطاق الطلب، لأنّه يحتاج متجر الطلب.
internal sealed class SearchLogBuffer : ISearchLog
{
    private readonly SearchLogChannel _channel;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;
    private readonly ILogger<SearchLogBuffer> _logger;
    private static int _dropped;

    public SearchLogBuffer(
        SearchLogChannel channel, ITenantContext tenant, TimeProvider clock, ILogger<SearchLogBuffer> logger)
    {
        _channel = channel; _tenant = tenant; _clock = clock; _logger = logger;
    }

    public void Record(string? term, int resultCount, string culture)
    {
        // لا شيء هنا يجوز أن يرمي: هذه الدالّة تُنادى من مسار البحث، وفشل التسجيل ليس فشل بحث.
        try
        {
            if (_tenant.Scope != TenantScope.Tenant) return;   // بحثٌ في نطاق المنصّة لا متجر له
            var entry = SearchQueryLog.For(term, resultCount, culture, _clock.GetUtcNow().UtcDateTime);
            if (entry is null) return;

            if (!_channel.TryWrite(new BufferedSearch(_tenant.RequireTenant().Id, entry))
                && Interlocked.Increment(ref _dropped) % 100 == 1)
                _logger.LogWarning(
                    "Search log buffer full; {Dropped} searches not recorded so far (capacity {Capacity})",
                    Volatile.Read(ref _dropped), SearchLogChannel.Capacity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search log entry discarded");
        }
    }
}

/// صفٌّ في الذاكرة بمعرّف متجره — المعرّف خارج الكيان لأنّ الكيان لا يملك setter له.
internal sealed record BufferedSearch(int TenantId, SearchQueryLog Entry);
