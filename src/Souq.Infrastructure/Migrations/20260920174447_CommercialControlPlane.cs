using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // C1 — مستوى التحكّم التجاري (ADR-0047). خمسة جداول جديدة، وتغييرٌ واحد على جدول قائم،
    // ونقلُ بياناتٍ مكتوبٌ باليد يجعل الاثنين **بلا أثر على أيّ متجر قائم**.
    //
    // التغيير: Tenants.EnabledModules كانت قيمته الافتراضية "كل الوحدات"، فصارت الفراغ. صفٌّ
    // يُدرَج بلا ذكر العمود كان يمنح كل شيء — راحةٌ مقبولة لثلاث ميزات اختيارية، وإهداءٌ للمنتج يوم
    // تصير الوحدة استحقاقاً مدفوعاً. **لا صفّ قائم يتغيّر**: الافتراضي يخصّ الإدراج وحده، والقيم
    // المخزّنة تبقى كما هي حرفاً.
    //
    // ونقل البيانات: تُنشأ "الخطة التأسيسية" — ليست شريحة تجارية ولا سعر لها — تمنح الوحدات
    // الاختيارية الثلاث المجّانية اليوم، ويُشترك عليها **كل متجر قائم**. بدونها كان C1 يُطفئ
    // كوبونات كل متجر وتقييماته ومفضّلته لحظةَ الترقية، لأن الاستحقاق صار يفشل مغلقاً.
    //
    // الرجوع (Down) يُسقط الجداول الخمسة فتذهب معها صفوف الخطة والاشتراكات، ويعيد الافتراضي القديم.
    // ============================================================================
    /// <inheritdoc />
    public partial class CommercialControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EnabledModules",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldDefaultValue: "promotions,reviews,wishlist");

            migrationBuilder.CreateTable(
                name: "EntitlementOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Entitlement = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitlementOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntitlementOverrides_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanEntitlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Entitlement = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanEntitlements_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanLimits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanLimits_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementOverrides_TenantId_ExpiresAtUtc",
                table: "EntitlementOverrides",
                columns: new[] { "TenantId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanEntitlements_PlanId_Entitlement",
                table: "PlanEntitlements",
                columns: new[] { "PlanId", "Entitlement" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanLimits_PlanId_Name",
                table: "PlanLimits",
                columns: new[] { "PlanId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code_Version",
                table: "Plans",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanId",
                table: "Subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantId",
                table: "Subscriptions",
                column: "TenantId",
                unique: true);

            // ────────────────────────────────────────────────────────────────────
            // نقل البيانات: الخطة التأسيسية، ثمّ اشتراك كل متجر قائم عليها.
            //
            // مكتوبٌ بـ SQL لا بكيانات (Migrations.md): الهجرة تعمل على **مخطّط تلك اللحظة**، ولو
            // كُتبت بكيان Plan لانكسرت يوم يتغيّر الكيان. القيم هنا نسخةٌ مجمَّدة عمداً:
            //   Status = 1  ⇒ PlanStatus.Published (منشورة كي يصحّ الاشتراك عليها)
            //   Status = 0  ⇒ SubscriptionStatus.Active
            // ومفاتيح الاستحقاقات هي StoreModules.All وقتَ C1؛ لا تُقرأ من الكود لنفس السبب.
            //
            // محتملةٌ للتكرار (WHERE NOT EXISTS في الموضعين): قاعدةٌ رُحّلت جزئياً ثمّ أُعيد تشغيلها
            // لا تُنشئ خطةً ثانية ولا تصطدم بالفهرس الفريد على TenantId.
            // ────────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                INSERT INTO Plans (Code, Version, Name, Status, CreatedAt)
                SELECT 'foundation', 1, N'الخطة التأسيسية', 1, SYSUTCDATETIME()
                WHERE NOT EXISTS (SELECT 1 FROM Plans WHERE Code = 'foundation' AND Version = 1);

                INSERT INTO PlanEntitlements (PlanId, Entitlement, CreatedAt)
                SELECT p.Id, k.Entitlement, SYSUTCDATETIME()
                FROM Plans p
                CROSS JOIN (VALUES ('promotions'), ('reviews'), ('wishlist')) AS k(Entitlement)
                WHERE p.Code = 'foundation' AND p.Version = 1
                  AND NOT EXISTS (SELECT 1 FROM PlanEntitlements e
                                  WHERE e.PlanId = p.Id AND e.Entitlement = k.Entitlement);

                INSERT INTO Subscriptions (TenantId, PlanId, Status, StartedAtUtc, CreatedAt)
                SELECT t.Id, p.Id, 0, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM Tenants t
                CROSS JOIN Plans p
                WHERE p.Code = 'foundation' AND p.Version = 1
                  AND NOT EXISTS (SELECT 1 FROM Subscriptions s WHERE s.TenantId = t.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntitlementOverrides");

            migrationBuilder.DropTable(
                name: "PlanEntitlements");

            migrationBuilder.DropTable(
                name: "PlanLimits");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.AlterColumn<string>(
                name: "EnabledModules",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "promotions,reviews,wishlist",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldDefaultValue: "");
        }
    }
}
