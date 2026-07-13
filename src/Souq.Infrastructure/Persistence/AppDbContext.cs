using Microsoft.EntityFrameworkCore;
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

    // اعتراض الحفظ لملء تواريخ الإنشاء/التعديل تلقائياً (Cross-Cutting).
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<Souq.Domain.Common.Entity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = DateTime.UtcNow;
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(ct);
    }
}
