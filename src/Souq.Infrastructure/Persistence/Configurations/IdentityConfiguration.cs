using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Identity;

namespace Souq.Infrastructure.Persistence.Configurations;

// جداول الهوية (وحدة Identity): حسابات المتاجر والمنصّة في جدول واحد (D-06)، TenantId NULL = منصّة.
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(User.FullNameMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.Role).HasMaxLength(30).IsRequired();
        builder.Property(u => u.Status).HasConversion<int>();
        builder.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
        // تجزئات SHA-256 بصيغة hex (64 حرفاً) — الرموز الخام لا تُخزَّن أبداً.
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(64);
        builder.Property(u => u.EmailVerificationTokenHash).HasMaxLength(64);
        builder.Ignore(u => u.BelongsToPlatform);   // مشتقّ من الدور

        // البريد فريد داخل كل متجر، وفريد بين حسابات المنصّة — فهرسان مرشَّحان لأن TenantId قد يكون NULL.
        builder.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique().HasFilter("[TenantId] IS NOT NULL");
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("[TenantId] IS NULL")
               .HasDatabaseName("IX_Users_NormalizedEmail_Platform");
        builder.HasIndex(u => u.PasswordResetTokenHash);
        builder.HasIndex(u => u.EmailVerificationTokenHash);

        // عدّاد المحاولات والقفل والختم يتغيّرون بالتزامن (دخولان معاً، تغيير كلمة مرور أثناء دخول).
        builder.HasRowVersion();
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.Property(t => t.RevokedReason).HasMaxLength(40);

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
