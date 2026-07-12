namespace Souq.Application.Common.Interfaces;

// ============================================================================
// عقد تخزين فيديوهات المنتجات — واجهة منفصلة عن IFileStorage (لا مشتركة) لأن
// لكل منها قواعد تحقّق مختلفة تماماً (نوع/حجم الملف) ومجلّداً عاماً مختلفاً،
// رغم تشابه الشكل ظاهرياً. نفس نمط عزل IPaymentService/IFileStorage.
// ============================================================================
public interface IVideoStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default);
}
