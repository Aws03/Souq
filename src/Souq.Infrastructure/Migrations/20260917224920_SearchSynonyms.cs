using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // مفردات بحث المتجر (M3، ADR-0042) — جدول جديد بالكامل، لا يلمس بياناً قائماً ولا ينقل شيئاً.
    //
    // كل فهرس يبدأ بـ TenantId: المفردات ملك المتجر، والكلمة نفسها في متجرين صفّان مستقلّان. الفريد على الصورة
    // المطبَّعة لا على ما كتبه التاجر — "مكنسة" و"مكنسه" لنفس المرادف صفّ واحد، لأنّهما كلمة واحدة في المطابقة.
    //
    // الرجوع يُسقط الجدول، فيفقد ما علّمه التاجر لمتجره. لا بيانات زبائن ولا مال، لكنّه عمل بشري:
    // نسخة احتياطية قبل رجوع مقصود (BackupAndRestore.md).
    // ============================================================================
    /// <inheritdoc />
    public partial class SearchSynonyms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchSynonyms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Term = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TermNormalized = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Expansion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpansionNormalized = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchSynonyms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchSynonyms_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchSynonyms_TenantId_Culture_TermNormalized",
                table: "SearchSynonyms",
                columns: new[] { "TenantId", "Culture", "TermNormalized" })
                .Annotation("SqlServer:Include", new[] { "ExpansionNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchSynonyms_TenantId_Culture_TermNormalized_ExpansionNormalized",
                table: "SearchSynonyms",
                columns: new[] { "TenantId", "Culture", "TermNormalized", "ExpansionNormalized" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchSynonyms");
        }
    }
}
