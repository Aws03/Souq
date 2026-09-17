using System.Text.Json;
using System.Text.Json.Serialization;

namespace Souq.API.Http;

// ============================================================================
// كل لحظة في ردود الـ API بتوقيت UTC وبلاحقة Z. التخزين UTC أصلاً (TimeProvider.GetUtcNow)، لكن DateTime يعود من
// قاعدة البيانات بلا Kind فكان يُكتب "2026-09-17T12:39:47" بلا لاحقة — والمتصفّح يقرأ نصّاً بلا لاحقة بتوقيته المحلي:
// آخر دخول وتاريخ الطلب وسطر التدقيق تنزاح بفرق توقيت القارئ (ثلاث ساعات لقارئ في عمّان). وما يُبنى في الذاكرة بـ
// DateTimeKind.Utc (فترات لوحة المؤشّرات) كان يُكتب بـ Z — عقدان لنوع واحد.
//
// الكتابة وحدها تتغيّر: القراءة من الطلبات كما كانت (reader.GetDateTime). ولا محوّل في EF: محوّل قيمة على DateTime
// يمنع ترجمة .Year و.Date في استعلامات التقارير.
// ============================================================================
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        });
}
