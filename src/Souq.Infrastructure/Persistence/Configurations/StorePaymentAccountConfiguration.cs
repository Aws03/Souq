using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// حساب بوّابة المتجر (المرحلة 11): صفّ واحد لكل متجر. السرّان نصّ مشفّر فقط (AES-GCM مربوط بالمتجر) — لا عمود لنصّهما.
//
// **وrowversion عليه (C12، جواب المالك D-13 = A).** كان الصفّ بلا حارس تزامن، فمديران يحرّران
// المفاتيح معاً يُنتجان «آخرُ كاتبٍ يفوز» صامتاً — والمفاتيحُ هنا ليست حقلَ عرض: الخاسرُ منهما
// يظنّ متجره يقبض في حسابٍ، وهو يقبض في آخر. وD-13 = A يجعل كلَّ متجرٍ تاجرَ نفسه، فالحساب
// إلزاميٌّ لا اختياري، وتبديلُ مفاتيحه عملٌ متكرّر لا حدثٌ نادر.
//
// والتعارضُ يُرفع 409 ولا يُعاد تلقائياً: إعادةُ المحاولة تكتب مفاتيح الثاني فوق مفاتيح الأوّل،
// وهو **العطبُ نفسه** بخطوةٍ إضافية. الصحيحُ أن يُقال للثاني «تغيّر الحساب، أعد التحميل».
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
        builder.HasRowVersion();
    }
}
