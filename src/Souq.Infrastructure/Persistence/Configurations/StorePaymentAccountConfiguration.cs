using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// حساب بوّابة المتجر (المرحلة 11): صفّ واحد لكل متجر. السرّان نصّ مشفّر فقط (AES-GCM مربوط بالمتجر) — لا عمود لنصّهما.
public class StorePaymentAccountConfiguration : IEntityTypeConfiguration<StorePaymentAccount>
{
    public void Configure(EntityTypeBuilder<StorePaymentAccount> builder)
    {
        builder.ToTable("StorePaymentAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Provider).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(a => a.PublishableKey).HasMaxLength(StorePaymentAccount.KeyMaxLength).IsUnicode(false).IsRequired();
        builder.Property(a => a.SecretKeyCipher).HasMaxLength(StorePaymentAccount.CipherMaxLength).IsUnicode(false).IsRequired();
        builder.Property(a => a.SecretKeyHint).HasMaxLength(16).IsRequired();
        builder.Property(a => a.WebhookSecretCipher).HasMaxLength(StorePaymentAccount.CipherMaxLength).IsUnicode(false);
        builder.HasIndex(a => a.TenantId).IsUnique();
    }
}
