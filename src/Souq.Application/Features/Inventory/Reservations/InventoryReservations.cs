using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Products.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Inventory.Reservations;

// مهلة الحجز (ما ينتظره المتجر ليدفع العميل قبل أن يعود المخزون للبيع) ودورة منسّق الانتهاء. تُقرأ من Inventory:*
// ويُتحقَّق منها عند الإقلاع (ADR-0020).
public sealed class InventorySettings
{
    public int ReservationMinutes { get; set; } = 30;

    // 0 = المنسّق الدوري معطّل (الاختبارات تشغّل أمر الانتهاء مباشرة).
    public int SweepIntervalSeconds { get; set; } = 60;

    public TimeSpan ReservationLifetime => TimeSpan.FromMinutes(ReservationMinutes);
}

// ============================================================================
// تنفيذ عقود Inventory. كل عملية: تحميل ⇒ تطبيق عبر InventoryItem ⇒ حفظ. عند تعارض rowversion (عملية أخرى غيّرت
// المخزون نفسه بين قراءتنا وحفظنا) ننسى النسخ المحمّلة ونعيد من القراءة: المحاولة التالية ترى المتاح الحقيقي، فإمّا
// تنجح أو ترفض بنفاد المخزون — فلا تُباع آخر قطعة مرّتين، ولا يخسر العميل بـ 409 عابر (Phase 0 C1).
// ============================================================================
public sealed class InventoryReservations : IInventoryReservations, IStockAvailability
{
    private readonly IInventoryRepository _inventory;
    private readonly IStockMovementRepository _movements;
    private readonly InventoryWriter _writer;
    private readonly InventorySettings _settings;
    private readonly TimeProvider _clock;

    public InventoryReservations(
        IInventoryRepository inventory, IStockMovementRepository movements, InventoryWriter writer,
        InventorySettings settings, TimeProvider clock)
    {
        _inventory = inventory; _movements = movements; _writer = writer; _settings = settings; _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task ReserveAsync(string reference, IReadOnlyList<ReservationLine> lines, CancellationToken ct)
    {
        // سطران لنفس المتغيّر حجز واحد بمجموعهما — المتاح يُقارن بالمجموع لا بكل سطر وحده.
        var wanted = lines.GroupBy(l => l.VariantId)
            .Select(g => (VariantId: g.Key, Quantity: g.Sum(l => l.Quantity), Name: g.First().DisplayName))
            .ToList();

        return _writer.SaveAsync(async () =>
        {
            var expiresAt = Now + _settings.ReservationLifetime;
            var items = (await _inventory.GetByVariantsAsync(wanted.Select(w => w.VariantId).ToList(), ct))
                .ToDictionary(i => i.VariantId);
            foreach (var (variantId, quantity, name) in wanted)
            {
                // متغيّر بلا مخزون مفتوح (بيانات معطوبة فقط) = لا شيء متاح.
                if (!items.TryGetValue(variantId, out var item))
                    throw new InsufficientStockException(name, quantity, 0);
                await _inventory.AddReservationAsync(item.Reserve(reference, quantity, expiresAt, name), ct);
            }
        }, ct);
    }

    public Task CommitAsync(string reference, CancellationToken ct) => _writer.SaveAsync(async () =>
    {
        var (reservations, items) = await LoadAsync(reference, ct);
        foreach (var reservation in reservations.Where(r => r.IsActive))
            if (items[reservation.InventoryItemId].Commit(reservation, Now) is { } sale)
                await _movements.AddAsync(sale, ct);
    }, ct);

    public Task CancelAsync(string reference, string reason, bool expired, CancellationToken ct) => _writer.SaveAsync(async () =>
    {
        var (reservations, items) = await LoadAsync(reference, ct);
        foreach (var reservation in reservations)
        {
            var item = items[reservation.InventoryItemId];
            if (reservation.IsActive)
                item.Release(reservation, Now, expired);
            else if (item.Restock(reservation, Now, reason) is { } restock)
                await _movements.AddAsync(restock, ct);
        }
    }, ct);

    public Task<IReadOnlyList<string>> FindExpiredAsync(int max, CancellationToken ct) =>
        _inventory.FindExpiredReferencesAsync(Now, max, ct);

    public async Task<IReadOnlyDictionary<int, int>> AvailableAsync(IReadOnlyCollection<int> variantIds, CancellationToken ct) =>
        (await _inventory.GetByVariantsAsync(variantIds, ct)).ToDictionary(i => i.VariantId, i => i.Available);

    private async Task<(IReadOnlyList<StockReservation> Reservations, Dictionary<int, InventoryItem> Items)> LoadAsync(
        string reference, CancellationToken ct)
    {
        var reservations = await _inventory.GetReservationsAsync(reference, ct);
        var items = reservations.Count == 0
            ? new Dictionary<int, InventoryItem>()
            : (await _inventory.GetByIdsAsync(reservations.Select(r => r.InventoryItemId).Distinct().ToList(), ct))
                .ToDictionary(i => i.Id);
        return (reservations, items);
    }
}

// ============================================================================
// حفظ تغييرات المخزون مع إعادة المحاولة عند تعارض التزامن: apply يحمّل من المستودع ويطبّق عبر الكيان، والحفظ بعده.
// عند التعارض يُنسى المحمَّل ويُعاد apply من قراءة جديدة. أي رفض آخر ينسى التغييرات الجزئية ثم يُرفع كما هو.
// داخل معاملة قائمة ينشئ EF نقطة حفظ قبل كل حفظ ويرجع إليها عند الفشل، فلا يبقى صفّ من المحاولة الفاشلة.
// ============================================================================
public sealed class InventoryWriter
{
    // كل تعارض يعني أن عملية أخرى حفظت على المخزون نفسه — خمس محاولات تتجاوز أي سباق واقعي على قطعة واحدة.
    public const int MaxAttempts = 5;

    private readonly IInventoryRepository _inventory;
    private readonly Souq.Domain.Interfaces.IUnitOfWork _uow;

    public InventoryWriter(IInventoryRepository inventory, Souq.Domain.Interfaces.IUnitOfWork uow)
    {
        _inventory = inventory; _uow = uow;
    }

    public async Task SaveAsync(Func<Task> apply, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await apply();
                await _uow.SaveChangesAsync(ct);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                _inventory.Reset();
            }
            catch
            {
                _inventory.Reset();
                throw;
            }
        }
    }
}

// ============================================================================
// تنفيذ منفذ Catalog (IVariantStockInitializer): مخزون جديد لمتغيّر جديد، وكمّيته الابتدائية حركة توريد — سجلّ
// المخزون يبدأ من نقطة معلومة. حفظان: معرّف المخزون قبل أول سطر سجلّ (المستدعي يلفّهما بمعاملته).
// ============================================================================
public sealed class VariantStockInitializer : IVariantStockInitializer
{
    private const string InitialStockNote = "المخزون الابتدائي عند إنشاء المنتج";

    private readonly IInventoryRepository _inventory;
    private readonly IStockMovementRepository _movements;
    private readonly Souq.Domain.Interfaces.IUnitOfWork _uow;

    public VariantStockInitializer(
        IInventoryRepository inventory, IStockMovementRepository movements, Souq.Domain.Interfaces.IUnitOfWork uow)
    {
        _inventory = inventory; _movements = movements; _uow = uow;
    }

    public async Task InitializeAsync(int productId, int variantId, int initialQuantity, int lowStockThreshold, CancellationToken ct)
    {
        var item = new InventoryItem(productId, variantId, lowStockThreshold);
        await _inventory.AddAsync(item, ct);
        await _uow.SaveChangesAsync(ct);

        if (initialQuantity > 0)
        {
            await _movements.AddAsync(item.Receive(initialQuantity, StockMovementType.Purchase, InitialStockNote), ct);
            await _uow.SaveChangesAsync(ct);
        }
    }
}
