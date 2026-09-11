using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Auditing;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// AppDbContext — جسر EF Core بين كياناتنا و SQL Server. هذا "تفصيل تقني"
// يعيش في Infrastructure، ولهذا لا يعرف Domain بوجوده إطلاقاً.
// ينفّذ IUnitOfWork: لأن SaveChanges في EF هي بطبيعتها معاملة ذرّية واحدة.
//
// عزل المستأجرين مركزي هنا (ADR-0005/0022، MultiTenancy.md §4): بالانعكاس — لا نسخاً يدوياً لكل
// كيان قد يُنسى — يحصل كل كيان على مرشّح استعلام عام مسمّى ("Tenant") ومفتاح أجنبي إلى Tenants:
//   ITenantOwned            ⇒ صفوف متجر السياق فقط؛ بلا متجر يرمي (لا "كل الصفوف" أبداً).
//   ITenantOrPlatformOwned  ⇒ (الحسابات وجلساتها) صفوف متجر السياق، أو صفوف المنصّة في نطاقها.
// المرشّح يقرأ النطاق عند تنفيذ كل استعلام (EF يعامل عضو السياق كمعامل). الكتابة يحرسها
// TenantWriteGuardInterceptor.
// ============================================================================
public class AppDbContext : DbContext, IUnitOfWork
{
    // اسم المرشّح: IgnoreQueryFilters([TenantFilter]) مسموح فقط في مسار المنصّة المُراجَع (اختبار معماري).
    public const string TenantFilter = "Tenant";

    private static readonly MethodInfo ConfigureTenantOwnedMethod =
        typeof(AppDbContext).GetMethod(nameof(ConfigureTenantOwned), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ConfigureTenantOrPlatformOwnedMethod =
        typeof(AppDbContext).GetMethod(nameof(ConfigureTenantOrPlatformOwned), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly ITenantContext _tenancy;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenancy) : base(options)
        => _tenancy = tenancy;

    internal ITenantContext Tenancy => _tenancy;

    // يُقيَّم عند تنفيذ كل استعلام على كيان ITenantOwned. بلا متجر ⇒ يرمي (حتى في نطاق المنصّة).
    private int CurrentTenantId => _tenancy.Tenant?.Id ?? throw new TenantContextMissingException();

    // للحسابات: متجر السياق، أو null في نطاق المنصّة (صفوف المنصّة)، وبلا نطاق يرمي.
    private int? CurrentScopeTenantId => _tenancy.Scope switch
    {
        TenantScope.Tenant => _tenancy.Tenant!.Id,
        TenantScope.Platform => null,
        _ => throw new TenantContextMissingException(),
    };

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<Basket> Baskets => Set<Basket>();
    public DbSet<OrderNumberSequence> OrderNumberSequences => Set<OrderNumberSequence>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<StorePaymentAccount> StorePaymentAccounts => Set<StorePaymentAccount>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();

    // الهوية (Identity): حسابات المتاجر وحسابات المنصّة في جدول واحد (D-06).
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // جداول المنصّة — بلا مرشّح مستأجر (هي ما يُعرِّفه).
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    // سجلّ التدقيق (D-17): للإضافة فقط، بلا مرشّح (يُقرأ من المنصّة بشرط صريح).
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // نطبّق كل ملفات الإعداد (Configurations) تلقائياً من هذا التجميع.
        // فصل الإعداد عن الكيان يُبقي الكيان نقياً من تفاصيل قاعدة البيانات.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        var entityTypes = modelBuilder.Model.GetEntityTypes().Where(t => !t.IsOwned()).Select(t => t.ClrType).ToList();
        foreach (var clrType in entityTypes.Where(typeof(ITenantOwned).IsAssignableFrom))
            ConfigureTenantOwnedMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
        foreach (var clrType in entityTypes.Where(typeof(ITenantOrPlatformOwned).IsAssignableFrom))
            ConfigureTenantOrPlatformOwnedMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);

        base.OnModelCreating(modelBuilder);
    }

    private void ConfigureTenantOwned<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
    {
        var entity = modelBuilder.Entity<T>();

        // لا يولّده EF ولا القاعدة أبداً: يختمه حارس الكتابة من السياق (وإلا ولّد EF قيمة مؤقتة لأنه
        // جزء من المفتاح البديل (TenantId, Id) الذي تشير إليه المراجع داخل المتجر).
        entity.Property<int>(nameof(ITenantOwned.TenantId)).ValueGeneratedNever();

        // الشريك الوحيد المسموح عبر الوحدات (النواة المشتركة): Restrict — لا يُمحى متجر ببياناته عرضاً.
        entity.HasOne<Tenant>().WithMany().HasForeignKey(nameof(ITenantOwned.TenantId)).OnDelete(DeleteBehavior.Restrict);

        entity.HasQueryFilter(TenantFilter, e => EF.Property<int>(e, nameof(ITenantOwned.TenantId)) == CurrentTenantId);
    }

    private void ConfigureTenantOrPlatformOwned<T>(ModelBuilder modelBuilder) where T : class, ITenantOrPlatformOwned
    {
        var entity = modelBuilder.Entity<T>();
        entity.Property<int?>(nameof(ITenantOrPlatformOwned.TenantId)).ValueGeneratedNever();
        entity.HasOne<Tenant>().WithMany()
              .HasForeignKey(nameof(ITenantOrPlatformOwned.TenantId))
              .IsRequired(false)
              .OnDelete(DeleteBehavior.Restrict);

        // المقارنة بمعامل قد يكون null تُترجم بدلالات C# (IS NULL في نطاق المنصّة).
        entity.HasQueryFilter(TenantFilter,
            e => EF.Property<int?>(e, nameof(ITenantOrPlatformOwned.TenantId)) == CurrentScopeTenantId);
    }

    // ترجمة استثناءات EF/SQL Server إلى أنواع Application — لا نوع تقني يعبر حدود هذه
    // الطبقة (ADR-0003). الـ API يترجم كليهما إلى 409 (ADR-0013). ختم تواريخ الإنشاء/
    // التعديل وختم المستأجر وحراسته في المعترِضات (تعمل داخل base.SaveChangesAsync).
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await base.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // rowversion تغيّر منذ القراءة: كتابة متزامنة سبقتنا على نفس التجمّع.
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 2601/2627 = انتهاك فهرس/قيد فريد — سباق تجاوز الفحص المبكر في المعالج.
            throw new UniqueConstraintViolationException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            // 547 = قيد مفتاح أجنبي: مرجع مفقود/لمتجر آخر، أو حذف سجلّ ما زال مُشاراً إليه.
            throw new ReferenceConstraintViolationException(ex);
        }
    }

    // معاملة صريحة لعدّة حفظات متتابعة (ADR-0021). داخل معاملة قائمة ⇒ تنضمّ إليها.
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        if (Database.CurrentTransaction is not null)
            return await work();

        await using var transaction = await Database.BeginTransactionAsync(ct);
        var result = await work();
        await transaction.CommitAsync(ct);
        return result;
    }

    public Task InTransactionAsync(Func<Task> work, CancellationToken ct = default) =>
        InTransactionAsync(async () => { await work(); return true; }, ct);
}
