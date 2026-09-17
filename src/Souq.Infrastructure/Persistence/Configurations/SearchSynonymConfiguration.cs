using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// مرادفات البحث (M3، ADR-0042). مفردات المتجر نفسه، فكل فهرس يبدأ بالمستأجر: الكلمة نفسها في متجرين صفّان
// مستقلّان تماماً، ولا يرى متجر مفردات غيره (مرشّح المستأجر يضمن ذلك، والفهرس يجعله رخيصاً).
//
// الفريد على (المستأجر، اللغة، الكلمة المطبَّعة، المرادف المطبَّع): الكلمة تتوسّع إلى أكثر من مرادف عمداً
// ("جوال" ⇒ "هاتف" و"موبايل")، فالفريد يمنع الصفّ المكرّر حرفياً لا التعدّد. وهو **على الصورة المطبَّعة**:
// إضافة "مكنسة" ثم "مكنسه" لنفس المرادف صفّ واحد لا صفّان، لأنّهما كلمة واحدة في المطابقة.
//
// TenantId ومرشّحه ومفتاحه الأجنبي إلى Tenants لا تُضبط هنا: حلقة الانعكاس في AppDbContext تفعل ذلك لكل
// ITenantOwned.
// ============================================================================
public class SearchSynonymConfiguration : IEntityTypeConfiguration<SearchSynonym>
{
    public void Configure(EntityTypeBuilder<SearchSynonym> builder)
    {
        builder.ToTable("SearchSynonyms");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Culture).HasMaxLength(CatalogTranslation.CultureMaxLength).IsRequired();
        builder.Property(s => s.Term).HasMaxLength(SearchSynonym.TermMaxLength).IsRequired();
        builder.Property(s => s.TermNormalized).HasMaxLength(SearchSynonym.TermMaxLength).IsRequired();
        builder.Property(s => s.Expansion).HasMaxLength(SearchSynonym.TermMaxLength).IsRequired();
        builder.Property(s => s.ExpansionNormalized).HasMaxLength(SearchSynonym.TermMaxLength).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.Culture, s.TermNormalized, s.ExpansionNormalized }).IsUnique();

        // مسار القراءة الساخن: كلمات الاستعلام تُبحث في هذا الفهرس مرّة واحدة لكل بحث، والمرادف عمود مُضمَّن
        // كي يُجاب الاستعلام من الفهرس بلا رجوع إلى الصفّ.
        builder.HasIndex(s => new { s.TenantId, s.Culture, s.TermNormalized })
            .IncludeProperties(s => s.ExpansionNormalized);
    }
}
