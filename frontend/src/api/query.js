// ============================================================================
// سلسلة الاستعلام لكل قوائم الـ API بقاعدة واحدة (كانت ثلاث نسخ مختلفة في client.js):
//   • القيم الفارغة (null/undefined/'') تُحذف — لا "keyword=" يصل الخادم.
//   • المصفوفات مفتاح متكرّر (categoryIds=1&categoryIds=2) — الصيغة التي يربطها ASP.NET
//     إلى List<int>، لا "1,2" المفصولة.
// ============================================================================
export function toQueryString(params = {}) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (isEmpty(value)) continue;
    if (Array.isArray(value)) value.filter((v) => !isEmpty(v)).forEach((v) => query.append(key, v));
    else query.append(key, value);
  }
  const text = query.toString();
  return text ? `?${text}` : '';
}

function isEmpty(value) {
  return value == null || value === '';
}
