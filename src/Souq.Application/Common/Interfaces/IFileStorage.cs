namespace Souq.Application.Common.Interfaces;

// ============================================================================
// عقد تخزين ملفات الوسائط (صور وفيديو المنتجات). نُعرّفه في Application وننفّذه في
// Infrastructure — قرص محلي في التطوير، وتخزين سحابي في الإنتاج، بتبديل تنفيذ واحد.
//
// folder: مجلّد منطقي ("images"، "videos"). extension: يحدّده المستدعي من نوع الملف
// المكتشَف فعلياً (MediaFileInspector) — التخزين لا يرى اسم الملف الأصلي أبداً
// (ADR-0016). يُعيد المسار العام للوصول للملف (مثل /uploads/images/{guid}.jpg).
// واجهة واحدة للصور والفيديو: كانتا واجهتين بتنفيذين متطابقين حرفياً (تكرار بلا فرق).
// ============================================================================
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string folder, string extension, CancellationToken ct = default);
}
