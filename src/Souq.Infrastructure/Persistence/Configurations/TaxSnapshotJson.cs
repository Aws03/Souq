using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// لقطةُ ضريبةِ الطلب ⇄ مستند JSON في عمودٍ واحد (`Orders.TaxSnapshot`) — نمطُ `StoreSettingsJson`
// نفسه ([ADR-0055](0055)).
//
// **ولماذا مستندٌ لا جدولُ أسطر؟** لأنّ اللقطة **لا تُستعلَم**: لا أحد يسأل «أيُّ الطلبات طُبِّقت
// عليها نسبةُ كذا» — مَن يسأل ذلك يسأل التجميعاتَ أو الفواتير. وما يُطلب منها شيءٌ واحد: أن تُقرأ
// كاملةً مع طلبها. وجدولُ أسطرٍ كان سيُضيف وصلاً في كل قراءةِ طلبٍ مقابل استعلامٍ لا يُطلب أبداً.
//
// والتعدادُ يُكتب **باسمه** لا برقمه: إعادةُ ترتيب تعدادٍ يوماً لا تُغيّر معنى لقطةٍ مكتوبة. وهو
// الدرسُ نفسه الذي يكتب به صندوقُ الصادر حمولاتَه.
//
// والنصوص العربية كما هي لا `\uXXXX`: لا تستهلك مساحةَ العمود ستّة أضعاف.
// ============================================================================
internal static class TaxSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly ValueConverter<TaxSnapshot?, string?> Converter = new(
        snapshot => snapshot == null ? null : JsonSerializer.Serialize(snapshot, Options),
        json => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<TaxSnapshot>(json, Options));

    // القيمة ثابتة: المساواةُ بالمستند المسلسَل، واللقطةُ هي الكائن نفسه (لا تُعدَّل بعد التجميد).
    public static readonly ValueComparer<TaxSnapshot?> Comparer = new(
        (a, b) => Json(a) == Json(b), snapshot => Json(snapshot).GetHashCode(), snapshot => snapshot);

    private static string Json(TaxSnapshot? snapshot) =>
        snapshot is null ? "" : JsonSerializer.Serialize(snapshot, Options);
}
