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
        builder.Ignore(p => p.ImplicitVariant);
        builder.Ignore(p => p.HasOptions);

        // المخزون يُعدَّل بالتزامن (شراءان معاً، أو شراء مع تعديل الإدارة) ⇒ rowversion يمنع البيع الزائد
        // والتحديث الضائع (Phase 0 C1/C4).
        builder.HasRowVersion();

        builder.HasMany(p => p.Translations).WithOne().HasForeignKey("ProductId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Images).WithOne().HasForeignKey("ProductId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Variants).WithOne().HasForeignKey("ProductId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Options).WithOne().HasForeignKey("ProductId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Options).UsePropertyAccessMode(PropertyAccessMode.Field);

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

        // ============================================================================
        // الصورة المطبَّعة للبحث (M3، ADR-0042). طول كل عمود كطول أصله: التطبيع لا يُطيل نصّ ar/en أبداً
        // (SearchTextTests.التطبيع_لا_يُطيل_نص_العربية_والإنجليزية_أبداً يحرس هذا)، فلا قصّ صامت عند الإدراج.
        //
        // الفهرس على (المستأجر، الاسم المطبَّع) يخدم ثلاث درجات من المطابقة: التطابق التامّ والبادئة بحثَ فهرس
        // (seek)، والاحتواء مسحَ فهرسٍ ضيّق بدل مسح جدول يحمل وصفاً بـ 4000 محرفاً. ومفتاح الجذر عمود مُضمَّن
        // كي يُجاب شرط EXISTS من الفهرس نفسه بلا رجوع إلى الصفّ.
        //
        // الوصف المطبَّع **غير مفهرس** عمداً: nvarchar(4000) يتجاوز حدّ مفتاح الفهرس في SQL Server (1700 بايت)،
        // فلا فهرس ممكن عليه أصلاً — ولذلك يبقى في أدنى درجة ترتيب، لا في مسار المطابقة السريع.
        // ============================================================================
        builder.Property(t => t.NameNormalized).HasMaxLength(CatalogText.NameMaxLength).IsRequired();
        builder.Property(t => t.DescriptionNormalized).HasMaxLength(CatalogText.DescriptionMaxLength);
        builder.HasIndex(nameof(CatalogTranslation.TenantId), nameof(CatalogTranslation.NameNormalized))
            .IncludeProperties(ownerKey, nameof(CatalogTranslation.Culture))
            .HasDatabaseName($"IX_{table}_TenantId_NameNormalized");
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
        // الافتراضي نشط دائماً (Product.DeactivateVariant يرفض) — والقاعدة خطّ الدفاع الأخير: منتج بلا متغيّر نشط يمثّله
        // يكسر كل عميل لا يعرف المتغيّرات.
        builder.ToTable("ProductVariants", table =>
            table.HasCheckConstraint("CK_ProductVariants_DefaultIsActive", "[IsDefault] = 0 OR [IsActive] = 1"));
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

        // تركيبة القيم فريدة داخل المنتج (ADR-0040): الفحص في Product، والفهرس يغلق سباق مديرَين. null لمنتج بلا خيارات.
        builder.Property(v => v.CombinationKey).HasMaxLength(ProductVariant.CombinationKeyMaxLength).IsUnicode(false);
        builder.HasIndex("ProductId", nameof(ProductVariant.CombinationKey)).IsUnique().HasFilter("[CombinationKey] IS NOT NULL");
        builder.HasMany(v => v.OptionValues).WithOne().HasForeignKey("ProductVariantId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.OptionValues).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex("ProductId", nameof(ProductVariant.IsDefault));
        builder.HasIndex("ProductId").IsUnique().HasFilter("[IsDefault] = 1")
               .HasDatabaseName("IX_ProductVariants_ProductId_Default");
    }
}

// ============================================================================
// خيارات المنتج وقيمها (ADR-0040): أبناء التجمّع بمفاتيح ظلّ، ونصوصها لكل لغة (فريدة لكل مالك). القيمة لها مفتاح بديل
// (TenantId, Id) كي يشير إليها متغيّر داخل متجرها وحده، والمرجع مقيَّد: قيمة يستخدمها متغيّر لا تُحذف حتى من القاعدة.
// ============================================================================
internal static class OptionTranslationMapping
{
    public static void Map<T>(EntityTypeBuilder<T> builder, string table, string ownerKey) where T : OptionTranslation
    {
        builder.ToTable(table);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Culture).HasMaxLength(CatalogTranslation.CultureMaxLength).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(OptionTranslation.NameMaxLength).IsRequired();
        builder.HasIndex(ownerKey, nameof(OptionTranslation.Culture)).IsUnique();
    }
}

public class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption>
{
    public void Configure(EntityTypeBuilder<ProductOption> builder)
    {
        builder.ToTable("ProductOptions");
        builder.HasKey(o => o.Id);
        builder.HasIndex("ProductId", nameof(ProductOption.Position));
        builder.HasMany(o => o.Translations).WithOne().HasForeignKey("ProductOptionId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.Values).WithOne().HasForeignKey("ProductOptionId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Values).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ProductOptionValueConfiguration : IEntityTypeConfiguration<ProductOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductOptionValue> builder)
    {
        builder.ToTable("ProductOptionValues");
        builder.HasKey(v => v.Id);
        builder.HasAlternateKey(v => new { v.TenantId, v.Id });
        builder.HasIndex("ProductOptionId", nameof(ProductOptionValue.Position));
        builder.HasMany(v => v.Translations).WithOne().HasForeignKey("ProductOptionValueId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ProductOptionTranslationConfiguration : IEntityTypeConfiguration<ProductOptionTranslation>
{
    public void Configure(EntityTypeBuilder<ProductOptionTranslation> builder) =>
        OptionTranslationMapping.Map(builder, "ProductOptionTranslations", "ProductOptionId");
}

public class ProductOptionValueTranslationConfiguration : IEntityTypeConfiguration<ProductOptionValueTranslation>
{
    public void Configure(EntityTypeBuilder<ProductOptionValueTranslation> builder) =>
        OptionTranslationMapping.Map(builder, "ProductOptionValueTranslations", "ProductOptionValueId");
}

public class ProductVariantOptionValueConfiguration : IEntityTypeConfiguration<ProductVariantOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductVariantOptionValue> builder)
    {
        builder.ToTable("ProductVariantOptionValues");
        builder.HasKey(v => v.Id);
        builder.HasOne(v => v.OptionValue).WithMany()
               .HasForeignKey(v => new { v.TenantId, v.OptionValueId })
               .HasPrincipalKey(v => new { v.TenantId, v.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex("ProductVariantId", nameof(ProductVariantOptionValue.OptionValueId)).IsUnique();
    }
}
