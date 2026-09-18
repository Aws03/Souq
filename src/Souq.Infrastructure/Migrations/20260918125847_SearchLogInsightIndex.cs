using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SearchLogInsightIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SearchQueryLogs_TenantId_Culture_TermNormalized",
                table: "SearchQueryLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_TenantId_Culture_TermNormalized_SearchedAt_Id",
                table: "SearchQueryLogs",
                columns: new[] { "TenantId", "Culture", "TermNormalized", "SearchedAt", "Id" },
                descending: new[] { false, false, false, true, true })
                .Annotation("SqlServer:Include", new[] { "ResultCount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SearchQueryLogs_TenantId_Culture_TermNormalized_SearchedAt_Id",
                table: "SearchQueryLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_TenantId_Culture_TermNormalized",
                table: "SearchQueryLogs",
                columns: new[] { "TenantId", "Culture", "TermNormalized" })
                .Annotation("SqlServer:Include", new[] { "ResultCount" });
        }
    }
}
