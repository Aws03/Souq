using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Files;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Stores;

// ============================================================================
// إعدادات المتجر من داخله (مدير المتجر، صلاحية store.settings.manage): المتجر هو متجر السياق دائماً — لا
// معرّف متجر في أي طلب هنا، فتعديل إعدادات متجر آخر مستحيل بالبناء لا بفحص قد يُنسى. كل تعديل يُبطل دليل
// المتاجر وإعداد الواجهة المخزَّنَين (يسري على هذه النسخة فوراً).
// ============================================================================

public record GetStoreSettingsQuery : IRequest<StoreSettingsDto>;

public class GetStoreSettingsHandler : IRequestHandler<GetStoreSettingsQuery, StoreSettingsDto>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantContext _context;

    public GetStoreSettingsHandler(ITenantRepository tenants, ITenantContext context)
    {
        _tenants = tenants; _context = context;
    }

    public async Task<StoreSettingsDto> Handle(GetStoreSettingsQuery query, CancellationToken ct) =>
        StoreSettingsMapper.ToDto(await StoreOf(_tenants, _context, ct));

    internal static async Task<Tenant> StoreOf(ITenantRepository tenants, ITenantContext context, CancellationToken ct) =>
        await tenants.GetByIdAsync(context.RequireTenant().Id, ct)
        ?? throw new InvalidOperationException("متجر السياق غير موجود في القاعدة");
}

public record UpdateStoreSettingsCommand(StoreSettingsInput Settings) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("store.settings.updated", "Tenant");
}

public sealed class UpdateStoreSettingsValidator : AbstractValidator<UpdateStoreSettingsCommand>
{
    public UpdateStoreSettingsValidator() =>
        RuleFor(x => x.Settings).NotNull().SetValidator(new StoreSettingsInputValidator());
}

public class UpdateStoreSettingsHandler : IRequestHandler<UpdateStoreSettingsCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantContext _context;
    private readonly ITenantDirectory _directory;
    private readonly IUnitOfWork _uow;

    public UpdateStoreSettingsHandler(ITenantRepository tenants, ITenantContext context, ITenantDirectory directory, IUnitOfWork uow)
    {
        _tenants = tenants; _context = context; _directory = directory; _uow = uow;
    }

    public async Task<Result> Handle(UpdateStoreSettingsCommand cmd, CancellationToken ct)
    {
        var tenant = await GetStoreSettingsHandler.StoreOf(_tenants, _context, ct);
        StoreSettingsEditor.Apply(tenant, cmd.Settings);
        await _uow.SaveChangesAsync(ct);
        await _directory.InvalidateAsync(ct);
        return Result.Success();
    }
}

// رفع ملف هوية (شعار، أيقونة، صورة مشاركة): النوع من محتوى الملف لا من اسمه (ADR-0016)، والمسار يولّده
// التخزين تحت بادئة المتجر — العميل لا يرسل رابطاً أبداً.
public record UploadStoreBrandingCommand(BrandingAsset Asset, Stream Content, long Length)
    : IRequest<Result<string>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("store.branding.uploaded", "Tenant",
        Metadata: new Dictionary<string, object?> { ["asset"] = Asset.ToString() });
}

public class UploadStoreBrandingHandler : IRequestHandler<UploadStoreBrandingCommand, Result<string>>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantContext _context;
    private readonly ITenantDirectory _directory;
    private readonly IFileStorage _storage;
    private readonly IUnitOfWork _uow;

    public UploadStoreBrandingHandler(
        ITenantRepository tenants, ITenantContext context, ITenantDirectory directory, IFileStorage storage, IUnitOfWork uow)
    {
        _tenants = tenants; _context = context; _directory = directory; _storage = storage; _uow = uow;
    }

    public async Task<Result<string>> Handle(UploadStoreBrandingCommand cmd, CancellationToken ct)
    {
        var type = await BrandingFiles.InspectAsync(cmd.Asset, cmd.Content, cmd.Length, ct);
        if (!type.IsSuccess) return Result<string>.Failure(type.Error!);

        var tenant = await GetStoreSettingsHandler.StoreOf(_tenants, _context, ct);
        var url = await _storage.SaveAsync(cmd.Content, BrandingFiles.Folder, type.Value!.Extension, ct);
        tenant.SetBrandingAsset(cmd.Asset, url);
        await _uow.SaveChangesAsync(ct);
        await _directory.InvalidateAsync(ct);
        return Result<string>.Success(url);
    }
}

// الصيغ المقبولة لكل ملف هوية — مشتركة بين رفع مدير المتجر ورفع المنصّة.
public static class BrandingFiles
{
    public const string Folder = "branding";
    public const long MaxBytes = 2 * 1024 * 1024;

    public static async Task<Result<MediaFileType>> InspectAsync(BrandingAsset asset, Stream content, long length, CancellationToken ct)
    {
        if (length > MaxBytes)
            return Result<MediaFileType>.Failure(Error.Validation("FileTooLarge", "ملف الهوية يتجاوز 2 ميغابايت"));

        MediaFileType[] allowed = asset switch
        {
            BrandingAsset.Logo => [MediaFileInspector.Png, MediaFileInspector.Jpeg, MediaFileInspector.Webp],
            BrandingAsset.Favicon => [MediaFileInspector.Png, MediaFileInspector.Ico],
            _ => [MediaFileInspector.Png, MediaFileInspector.Jpeg],
        };
        var type = await MediaFileInspector.DetectAsync(content, ct);
        return type is not null && allowed.Contains(type)
            ? Result<MediaFileType>.Success(type)
            : Result<MediaFileType>.Failure(Error.Validation("UnsupportedMediaType",
                $"الصيغ المقبولة: {string.Join("، ", allowed.Select(t => t.Extension.TrimStart('.').ToUpperInvariant()))}"));
    }
}

// ============================================================================
// إعداد الواجهة العام لمتجر المضيف (D-12): عقد وحدة Platform (Modules.md: IStoreConfiguration) — مخزَّن
// مؤقتاً لكل متجر ويُبطَل مع دليل المتاجر عند كل تعديل. الواجهة تبني عليه الهوية واللغة والعملة والوحدات.
// ============================================================================
public interface IStoreConfiguration
{
    Task<StorefrontConfigDto?> GetStorefrontAsync(int tenantId, CancellationToken ct);
}

public record GetStorefrontConfigQuery : IRequest<StorefrontConfigDto>;

public class GetStorefrontConfigHandler : IRequestHandler<GetStorefrontConfigQuery, StorefrontConfigDto>
{
    private readonly IStoreConfiguration _configuration;
    private readonly ITenantContext _context;

    public GetStorefrontConfigHandler(IStoreConfiguration configuration, ITenantContext context)
    {
        _configuration = configuration; _context = context;
    }

    public async Task<StorefrontConfigDto> Handle(GetStorefrontConfigQuery query, CancellationToken ct)
    {
        var store = _context.RequireTenant();
        var config = await _configuration.GetStorefrontAsync(store.Id, ct)
            ?? throw new InvalidOperationException("متجر السياق غير موجود في القاعدة");

        // ============================================================================
        // الوحدات تُؤخذ من **لقطة الطلب** لا من عمود المتجر (C1، ADR-0047 §4). الفرق ليس تجميلاً:
        // اللقطة هي الجواب الذي تفرضه TenantAvailability، والعمود هو أحد مدخليه. لو أعلنت هذه
        // النقطة العمود لأعلنت للواجهة وحدةً يردّ عليها الخادم 404 — واجهةٌ تعرض ما لا يعمل.
        // ============================================================================
        return config with { Modules = store.Modules.Order(StringComparer.Ordinal).ToList() };
    }
}
