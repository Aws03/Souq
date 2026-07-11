namespace Souq.Application.Common.Interfaces;

// ============================================================================
// عقد تخزين الملفات (صور المنتجات). نُعرّفه في Application وننفّذه في
// Infrastructure — نفس نمط IPaymentService. المنطق لا يعرف "أين" تُخزَّن الصورة:
// قرص محلي في التطوير، وتخزين سحابي (S3/Azure Blob) في الإنتاج، بتبديل تنفيذ واحد.
// يُعيد المسار العام للوصول للملف (مثل /uploads/{guid}.jpg).
// ============================================================================
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default);
}
