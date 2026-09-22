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
using Souq.Infrastructure.Persistence.Outbox;

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
    private readonly TimeProvider _clock;

    // الساعة لوقت أحداث المجال في صندوق الصادر (المرحلة 14) — اختيارية لسياقات تُبنى يدوياً (اختبارات النموذج).
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenancy, TimeProvider? clock = null) : base(options)
    {
        _tenancy = tenancy;
        _clock = clock ?? TimeProvider.System;
    }

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
    // مفردات بحث المتجر (M3، ADR-0042): يقرؤها مسار البحث ويكتبها التاجر من لوحته.
    public DbSet<SearchSynonym> SearchSynonyms => Set<SearchSynonym>();
    // أثر ما بحث عنه المتسوّقون (M13): يكتبه كاتبٌ خلفي على دفعات، وتقرؤه لوحة التاجر ليُصلح المفردات أعلاه.
    public DbSet<SearchQueryLog> SearchQueryLogs => Set<SearchQueryLog>();

    // الالتقاط السلوكي (C9، ADR-0050): الأحداث الخام، وربطُ الزائر بالعميل في جدولٍ منفصل عنها
    // عمداً، والتجميعات التي تبقى بعد مسحها، وعلامةُ «إلى أيّ يومٍ جُمِّع».
    public DbSet<BehaviouralEvent> BehaviouralEvents => Set<BehaviouralEvent>();
    public DbSet<VisitorIdentityLink> VisitorIdentityLinks => Set<VisitorIdentityLink>();
    public DbSet<ProductEngagementDaily> ProductEngagementDailies => Set<ProductEngagementDaily>();
    public DbSet<ProductPairDaily> ProductPairDailies => Set<ProductPairDaily>();
    public DbSet<AnalyticsRollupState> AnalyticsRollupStates => Set<AnalyticsRollupState>();

    // الضريبة (ADR-0055، قرار المالك P-06): ملفُّ اختصاصٍ تحفظه المنصّة وإصداراتُه ونسبُه — جداولُ
    // منصّة بلا متجر، كـ Plan — وإعدادُ كلّ متجر: أيَّ ملفٍّ اختار وهل فعّل الجمع.
    public DbSet<TaxProfile> TaxProfiles => Set<TaxProfile>();
    public DbSet<TaxProfileVersion> TaxProfileVersions => Set<TaxProfileVersion>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<StoreTaxSettings> StoreTaxSettings => Set<StoreTaxSettings>();
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
    // عدّاد حصص المتجر (C2): كيان متجر مُرشَّح كسائرها — لا جدول منصّة رغم أن الحدّ يأتي من الخطة.
    public DbSet<TenantUsageCounter> TenantUsageCounters => Set<TenantUsageCounter>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<StorePaymentAccount> StorePaymentAccounts => Set<StorePaymentAccount>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // صندوق الصادر (المرحلة 14): كتلة بناء بلا مرشّح مستأجر — يقرؤها المُرسِل عبر المتاجر.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // الهوية (Identity): حسابات المتاجر وحسابات المنصّة في جدول واحد (D-06).
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // جداول المنصّة — بلا مرشّح مستأجر (هي ما يُعرِّفه).
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    // ============================================================================
    // مستوى التحكّم التجاري (C1، وحدة Billing، ADR-0047). ثلاثة جداول بشكلين:
    //   • Plans/PlanEntitlements/PlanLimits — الشكل C: عالمية بلا متجر، تُقرأ كأي استعلام.
    //   • Subscriptions/EntitlementOverrides — الشكل **B**: تحمل TenantId ولا مرشّح عليها.
    //     عزلها كلّه شرطٌ صريح يكتبه المستدعي، ويحرسه
    //     `قراءة_جداول_المنصّة_بمفتاح_متجر_محصورة_في_مسارها_المراجَع` في TenancyRuleTests.
    // ============================================================================
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();
    public DbSet<PlanLimit> PlanLimits => Set<PlanLimit>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<EntitlementOverride> EntitlementOverrides => Set<EntitlementOverride>();

    // ============================================================================
    // فوترةُ التاجر (C5، ADR-0056). الشكلان نفساهما:
    //   • PlatformBillingSettings وPlatformDocumentSequences — الشكل C: صفٌّ عالميّ بلا متجر.
    //   • PlatformInvoices وCreditNotes وBillingPeriods وBillableEvents — الشكل **B**: تحمل
    //     TenantId ولا مرشّح عليها. وهو مقصود: الفاتورة يجب أن تبقى مقروءةً بعد أرشفة متجرها.
    //
    // والأبناء (الأسطر، المسدَّدات) بلا TenantId: يُقرأون مع جذرهم ولا يُستعلَمون وحدهم، فحملُهم
    // مفتاحَ متجرٍ كان سيوحي بأنّهم يُقرأون مستقلّين.
    // ============================================================================
    public DbSet<PlatformBillingSettings> PlatformBillingSettings => Set<PlatformBillingSettings>();
    public DbSet<PlatformDocumentSequence> PlatformDocumentSequences => Set<PlatformDocumentSequence>();
    public DbSet<PlatformInvoice> PlatformInvoices => Set<PlatformInvoice>();
    public DbSet<PlatformInvoiceLine> PlatformInvoiceLines => Set<PlatformInvoiceLine>();
    public DbSet<PlatformInvoicePayment> PlatformInvoicePayments => Set<PlatformInvoicePayment>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();
    public DbSet<BillingPeriod> BillingPeriods => Set<BillingPeriod>();
    public DbSet<BillableEvent> BillableEvents => Set<BillableEvent>();

    // C4 (ADR-0057): عقودُ الإيجار المُسمّاة — الشكل C، عالميّة بلا متجر. صفٌّ واحد لكل عملٍ
    // لا يجوز أن تُجريه نسختان معاً.
    public DbSet<DistributedLease> DistributedLeases => Set<DistributedLease>();

    // C4 (ADR-0057): إشاراتُ إبطالِ الذاكرات — الشكل C. صفٌّ لكل ذاكرة، وعدّادُ جيلٍ يتزايد.
    public DbSet<CacheSignal> CacheSignals => Set<CacheSignal>();

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
        var raised = CaptureDomainEvents();
        try
        {
            var written = await base.SaveChangesAsync(ct);
            raised.Complete();
            return written;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // rowversion تغيّر منذ القراءة: كتابة متزامنة سبقتنا على نفس التجمّع.
            raised.Abandon(this);
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 2601/2627 = انتهاك فهرس/قيد فريد — سباق تجاوز الفحص المبكر في المعالج.
            raised.Abandon(this);
            throw new UniqueConstraintViolationException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            // 547 = قيد مفتاح أجنبي: مرجع مفقود/لمتجر آخر، أو حذف سجلّ ما زال مُشاراً إليه.
            raised.Abandon(this);
            throw new ReferenceConstraintViolationException(ex);
        }
        catch (Exception ex) when (IsDeadlock(ex))
        {
            // ====================================================================
            // 1205 = جمود. **وهذا السطر كان ناقصاً، والنقص كان غير متماثل:** `InTransactionAsync`
            // أدناه تترجم الجمود منذ F-7 ومعها تعليقٌ يقول لماذا بالحرف — "بلا الترجمة يخرج الخطأ
            // نوعاً تقنياً من SQL Server عبر حدود الطبقة ويصير 500، وهي رسالة خاطئة لسباق قابل
            // للإعادة" — بينما `SaveChangesAsync` بلا معاملة صريحة كانت تُخرجه خاماً.
            //
            // فالنتيجة أن الجمود نفسه كان **409 يُعاد** داخل معاملة و**500** خارجها. ومسار قراءة
            // السلّة يكتب بلا معاملة صريحة (الدمج ثم الحذف)، فإعادةُ المحاولة التي بناها F-28 في
            // `BasketWriter` لم تكن تراه أصلاً: تلتقط ConcurrencyConflictException وUnique، ولا
            // شيء يصل إليها. قِيس في مجموعة التكامل كاملةً: `GET /api/basket` يجيب 500 بخطأ 1205
            // (F-30) — ويمرّ الاختبار وحده لأن الجمود يحتاج حِملاً.
            //
            // والترجمة هي الجواب لا إعادةُ المحاولة هنا: الضحية تُرجَع كاملةً فلا بيانات ناقصة،
            // والمطلوب من المتصل إعادة المحاولة — وهو بالضبط معنى هذا النوع في هذا المستودع.
            // ====================================================================
            raised.Abandon(this);
            throw new ConcurrencyConflictException(ex);
        }
        catch
        {
            raised.Abandon(this);
            throw;
        }
    }

    // أحداث المجال ⇒ صفوف صادر في الحفظ نفسه (المرحلة 14، ADR-0034): تُلتزم مع التغيير أو تتراجع معه. بعد النجاح تُمحى من
    // الكيانات؛ بعد الفشل تُفصل الصفوف المضافة وتُمحى الأحداث أيضاً — من يعيد المحاولة يعيد بناء تغييره فتُرفع أحداثه من جديد.
    private RaisedEvents CaptureDomainEvents()
    {
        var sources = ChangeTracker.Entries<BaseEntity<int>>()
            .Select(e => e.Entity)
            .Where(e => e.PendingDomainEvents().Count > 0)
            .ToList();
        if (sources.Count == 0) return RaisedEvents.None;

        int? tenantId = _tenancy.Scope == TenantScope.Tenant ? _tenancy.Tenant!.Id : null;
        var now = _clock.GetUtcNow().UtcDateTime;
        var messages = sources.SelectMany(e => e.PendingDomainEvents()).Select(e => OutboxMessage.For(e, tenantId, now)).ToList();
        OutboxMessages.AddRange(messages);
        return new RaisedEvents(sources, messages);
    }

    private sealed record RaisedEvents(IReadOnlyList<BaseEntity<int>> Sources, IReadOnlyList<OutboxMessage> Messages)
    {
        public static readonly RaisedEvents None = new([], []);

        public void Complete()
        {
            foreach (var source in Sources) source.ClearDomainEvents();
        }

        public void Abandon(DbContext db)
        {
            foreach (var message in Messages) db.Entry(message).State = EntityState.Detached;
            Complete();
        }
    }

    // معاملة صريحة لعدّة حفظات متتابعة (ADR-0021). داخل معاملة قائمة ⇒ تنضمّ إليها.
    //
    // الجمود (deadlock، خطأ 1205) يُترجَم هنا كما تُترجَم بقية أخطاء التزامن: معاملتان تقرأ كلّ
    // منهما صفّاً كتبته الأخرى ولم تلتزم بعد — وهو ما يحدث فعلاً حين يوقف طلبان متزامنان مديرَين
    // مختلفين (F-7). الضحية تُرجَع كاملةً، فلا بيانات ناقصة؛ والمطلوب من المتصل إعادة المحاولة،
    // وهذا بالضبط معنى ConcurrencyConflictException في هذا المستودع. بلا الترجمة يخرج الخطأ
    // نوعاً تقنياً من SQL Server عبر حدود الطبقة ويصير 500، وهي رسالة خاطئة لسباق قابل للإعادة.
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        if (Database.CurrentTransaction is not null)
            return await work();

        await using var transaction = await Database.BeginTransactionAsync(ct);
        T result;
        try
        {
            result = await work();
        }
        catch (Exception ex) when (IsDeadlock(ex))
        {
            throw new ConcurrencyConflictException(ex);
        }
        await transaction.CommitAsync(ct);
        return result;
    }

    private static bool IsDeadlock(Exception exception) => exception switch
    {
        SqlException { Number: 1205 } => true,
        { InnerException: { } inner } => IsDeadlock(inner),
        _ => false,
    };

    public Task InTransactionAsync(Func<Task> work, CancellationToken ct = default) =>
        InTransactionAsync(async () => { await work(); return true; }, ct);

    // ========================================================================
    // معاملة بمستوى عزل مطلوب (F-29). الترجمة إلى `System.Data.IsolationLevel` تقع **هنا**: المجال
    // يسمّي النيّة، وهذه الطبقة وحدها تعرف مفردات المزوّد.
    //
    // ونفس ترجمة الجمود تنطبق: SERIALIZABLE يجعل الجمود **متوقَّعاً** لا نادراً — متسابقان يأخذان
    // أقفال مدى مشتركة ثمّ يطلبان الحصري. وهو مقبول هنا لأن الضحية تُرجَع كاملةً ويصل المتصل
    // `ConcurrencyConflictException`، أي "أعد المحاولة" — وهو ما يُترجمه المستدعي إلى رفض عمل واضح.
    // ========================================================================
    public async Task InTransactionAsync(
        Func<Task> work, TransactionIsolation isolation, CancellationToken ct = default)
    {
        // داخل معاملة قائمة ⇒ ننضمّ إليها: مستوى المعاملة يُحدَّد عند فتحها ولا يُرفَع في منتصفها.
        if (Database.CurrentTransaction is not null || isolation == TransactionIsolation.Default)
        {
            await InTransactionAsync(work, ct);
            return;
        }

        await using var transaction = await Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        try
        {
            await work();
        }
        catch (Exception ex) when (IsDeadlock(ex))
        {
            throw new ConcurrencyConflictException(ex);
        }
        await transaction.CommitAsync(ct);
    }
}
