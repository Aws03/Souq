using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // الصورة المطبَّعة لنصوص الكتالوج (M3، ADR-0042) — إضافية بالكامل، لا تنقل بياناً ولا تتلف شيئاً.
    //
    // ما تضيفه: عمودا NameNormalized (إلزامي، افتراضه "") و DescriptionNormalized (اختياري) على جدولَي
    // ProductTranslations و CategoryTranslations، وفهرساً على (TenantId, NameNormalized) يُضمِّن مفتاح الجذر
    // واللغة — فيُجاب شرط EXISTS في مسار البحث من الفهرس وحده.
    //
    // **الصفوف القائمة تبقى بصورة فارغة عند الترقية، وتُعبَّأ عند أول إقلاع** بـ SearchIndexBackfill لا بـ SQL
    // هنا: التطبيع دالّة Unicode في المجال (SearchText.Normalize)، وكتابتها ثانيةً بـ T-SQL تعني تنفيذين لقاعدة
    // واحدة وتباعداً صامتاً بينهما يجعل النص المفهرس لا يطابق نصّ الاستعلام. شرط التعبئة (NameNormalized = '')
    // بحث فهرس في الفهرس الجديد نفسه، فالتكلفة على الإقلاع المعتاد لا تُذكر.
    //
    // فهرسا IX_*_TenantId المفردان يُسقَطان: الفهرس الجديد يبدأ بـ TenantId فيخدم كل ما كانا يخدمانه، وEF أسقطهما
    // من تلقائه لهذا السبب (ForeignKeyIndexConvention). فهرس أقلّ = كتابة أسرع، ولا قدرة قراءة تُفقَد. والرجوع
    // يعيدهما كما كانا.
    // ============================================================================
    /// <inheritdoc />
    public partial class SearchNormalizedCatalogText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductTranslations_TenantId",
                table: "ProductTranslations");

            migrationBuilder.DropIndex(
                name: "IX_CategoryTranslations_TenantId",
                table: "CategoryTranslations");

            migrationBuilder.AddColumn<string>(
                name: "DescriptionNormalized",
                table: "ProductTranslations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "ProductTranslations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DescriptionNormalized",
                table: "CategoryTranslations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "CategoryTranslations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTranslations_TenantId_NameNormalized",
                table: "ProductTranslations",
                columns: new[] { "TenantId", "NameNormalized" })
                .Annotation("SqlServer:Include", new[] { "ProductId", "Culture" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTranslations_TenantId_NameNormalized",
                table: "CategoryTranslations",
                columns: new[] { "TenantId", "NameNormalized" })
                .Annotation("SqlServer:Include", new[] { "CategoryId", "Culture" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductTranslations_TenantId_NameNormalized",
                table: "ProductTranslations");

            migrationBuilder.DropIndex(
                name: "IX_CategoryTranslations_TenantId_NameNormalized",
                table: "CategoryTranslations");

            migrationBuilder.DropColumn(
                name: "DescriptionNormalized",
                table: "ProductTranslations");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "ProductTranslations");

            migrationBuilder.DropColumn(
                name: "DescriptionNormalized",
                table: "CategoryTranslations");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "CategoryTranslations");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTranslations_TenantId",
                table: "ProductTranslations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTranslations_TenantId",
                table: "CategoryTranslations",
                column: "TenantId");
        }
    }
}
