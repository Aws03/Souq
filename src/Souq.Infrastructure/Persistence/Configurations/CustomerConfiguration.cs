using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FullName).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Role).HasMaxLength(20).IsRequired();

        // البريد هو هوية الدخول — يجب أن يكون فريداً على مستوى قاعدة البيانات
        // (القاعدة النهائية للحقيقة)، لا في الكود فقط حيث تتسابق الطلبات المتزامنة.
        builder.HasIndex(c => c.Email).IsUnique();
    }
}
