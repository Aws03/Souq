using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// إعداد جدول المنتجات بـ Fluent API — لا Data Annotations على الكيان كي يبقى Domain نقياً من EF/SQL.
// المرحلة 5: النصوص والصور والمتغيّرات جداول أبناء بمفتاح ظلّ ProductId (تُحذف مع الجذر، والجذر يُؤرشف لا يُحذف).
// ============================================================================
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Slug).HasMaxLength(Product.SlugMaxLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Brand).HasMaxLength(Product.BrandMaxLength);
        builder.Property(p => p.VideoUrl).HasMaxLength(Product.VideoUrlMaxLength);

        // مشتقّة من الأبناء أو من غيرها — لا أعمدة لها.
        builder.Ignore(p => p.Name);
        builder.Ignore(p => p.Price);
        builder.Ignore(p => p.CompareAtPrice);
        builder.Ignore(p => p.Sku);
        builder.Ignore(p => p.IsActive);
        builder.Ignore(p => p.PrimaryImageUrl);
        builder.Ignore(p => p.DefaultVariant);

        // المخزون يُعدَّل بالتزامن (شراءان معاً، أو شراء مع تعديل الإدارة) ⇒ rowversion يمنع البيع الزائد
        // والتحديث الضائع (Phase 0 C1/C4).
        builder.HasRowVersion();

        builder.HasMany(p => p.Translations).WithOne().HasForeignKey("ProductId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Images).WithOne().HasForeignKey("ProductId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Variants).WithOne().HasForeignKey("ProductId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Restrict لا Cascade: حذف فئة لا يجوز أن يمحو منتجاتها. المفتاح داخل المتجر (TenantId, CategoryId) ⇒
        // (TenantId, Id): يستحيل حتى على مستوى القاعدة أن يشير منتج لفئة متجر آخر (MultiTenancy.md §6).
        builder.HasOne(p => p.Category)
               .WithMany()
               .HasForeignKey(p => new { p.TenantId, p.CategoryId })
               .HasPrincipalKey(c => new { c.TenantId, c.Id })
               .OnDelete(DeleteBehavior.Restrict);

        // معرّف الرابط فريد داخل المتجر؛ الكتالوج العام يصفّي بالحالة والفئة — الفهارس تبدأ بالمستأجر.
        builder.HasIndex(p => new { p.TenantId, p.Slug }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Status, p.CategoryId });
    }
}

// ترجمات الكتالوج: لغة واحدة لكل جذر (فهرس فريد).
internal static class CatalogTranslationMapping
{
    public static void Map<T>(EntityTypeBuilder<T> builder, string table, string ownerKey) where T : CatalogTranslation
    {
        builder.ToTable(table);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Culture).HasMaxLength(CatalogTranslation.CultureMaxLength).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(CatalogText.NameMaxLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(CatalogText.DescriptionMaxLength);
        builder.Property(t => t.MetaTitle).HasMaxLength(CatalogText.MetaTitleMaxLength);
        builder.Property(t => t.MetaDescription).HasMaxLength(CatalogText.MetaDescriptionMaxLength);
        builder.HasIndex(ownerKey, nameof(CatalogTranslation.Culture)).IsUnique();
    }
}

public class ProductTranslationConfiguration : IEntityTypeConfiguration<ProductTranslation>
{
    public void Configure(EntityTypeBuilder<ProductTranslation> builder) =>
        CatalogTranslationMapping.Map(builder, "ProductTranslations", "ProductId");
}

public class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> builder) =>
        CatalogTranslationMapping.Map(builder, "CategoryTranslations", "CategoryId");
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Url).HasMaxLength(ProductImage.UrlMaxLength).IsRequired();
        builder.HasIndex("ProductId", nameof(ProductImage.SortOrder));
    }
}

// ============================================================================
// المتغيّرات (D-21): السعر كائن قيمة (عمودان)، وسعر المقارنة مبلغ فقط بعملة السعر نفسها. SKU فريد داخل المتجر حين
// يُضبط، ومتغيّر افتراضي واحد بالضبط لكل منتج (فهرس فريد مرشَّح — القاعدة الحارس الأخير). المفتاح البديل
// (TenantId, Id) لمراجع داخل المتجر من المخزون (المرحلة 6) وسطور الطلب (المرحلة 9).
// ============================================================================
public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants");
        builder.HasKey(v => v.Id);
        builder.HasAlternateKey(v => new { v.TenantId, v.Id });
        builder.Property(v => v.Sku).HasMaxLength(ProductVariant.SkuMaxLength);

        builder.OwnsOne(v => v.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Price").HasColumnType(PersistenceConventions.MoneyColumnType);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Property<decimal?>("_compareAtAmount")
               .HasColumnName("CompareAtPrice")
               .HasColumnType(PersistenceConventions.MoneyColumnType);
        builder.Ignore(v => v.CompareAtPrice);
        builder.Ignore(v => v.IsOnSale);

        builder.HasIndex(v => new { v.TenantId, v.Sku }).IsUnique().HasFilter("[Sku] IS NOT NULL");
        builder.HasIndex("ProductId", nameof(ProductVariant.IsDefault));
        builder.HasIndex("ProductId").IsUnique().HasFilter("[IsDefault] = 1")
               .HasDatabaseName("IX_ProductVariants_ProductId_Default");
    }
}
