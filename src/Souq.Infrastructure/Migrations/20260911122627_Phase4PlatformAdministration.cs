using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // Phase 4 — إدارة المنصّة (إضافية بالكامل، لا حذف ولا نقل بيانات):
    //   Tenants.EnabledModules — افتراضيها للصفوف القائمة كل الوحدات: متجر قائم لا يفقد كوبوناته وتقييماته.
    //   Tenants.Settings       — مستند JSON يقبل NULL (= الإعدادات الافتراضية)؛ البذر يضبط المتجر الافتراضي بمظهر ماركة.
    //   AuditEntries           — سجلّ التدقيق (للإضافة فقط، بلا مفتاح أجنبي ولا مرشّح مستأجر).
    // ============================================================================
    /// <inheritdoc />
    public partial class Phase4PlatformAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnabledModules",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "promotions,reviews,wishlist");

            migrationBuilder.AddColumn<string>(
                name: "Settings",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TargetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TargetId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ActorUserId_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                table: "AuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "TenantId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "EnabledModules",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Settings",
                table: "Tenants");
        }
    }
}
