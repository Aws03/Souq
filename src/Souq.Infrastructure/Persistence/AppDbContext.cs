using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// AppDbContext — جسر EF Core بين كياناتنا و SQL Server. هذا "تفصيل تقني"
// يعيش في Infrastructure، ولهذا لا يعرف Domain بوجوده إطلاقاً.
// ينفّذ IUnitOfWork: لأن SaveChanges في EF هي بطبيعتها معاملة ذرّية واحدة.
// ============================================================================
public class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // نطبّق كل ملفات الإعداد (Configurations) تلقائياً من هذا التجميع.
        // فصل الإعداد عن الكيان يُبقي الكيان نقياً من تفاصيل قاعدة البيانات.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    // اعتراض الحفظ لملء تواريخ الإنشاء/التعديل تلقائياً (Cross-Cutting)، ولترجمة
    // استثناءات EF/SQL Server إلى أنواع Application — لا نوع تقني يعبر حدود هذه الطبقة
    // (ADR-0003). الـ API يترجم كليهما إلى 409 (ADR-0013).
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<Souq.Domain.Common.Entity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = DateTime.UtcNow;
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

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
    }
}
