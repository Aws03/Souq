using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase10Coupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxUsesPerCustomer",
                table: "Coupons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "Coupons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Coupons_TenantId_Id",
                table: "Coupons",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "CouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    CouponId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Coupons_TenantId_CouponId",
                        columns: x => new { x.TenantId, x.CouponId },
                        principalTable: "Coupons",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Customers_TenantId_CustomerId",
                        columns: x => new { x.TenantId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Orders_TenantId_OrderId",
                        columns: x => new { x.TenantId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_TenantId_CouponId_CustomerId_Status",
                table: "CouponRedemptions",
                columns: new[] { "TenantId", "CouponId", "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_TenantId_CustomerId",
                table: "CouponRedemptions",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_TenantId_OrderId",
                table: "CouponRedemptions",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

            // نقل إضافي بلا حذف (ADR-0030): كل طلب قائم استخدم كوبوناً ما زال موجوداً يأخذ سجلّ استخدامه — المعلّق محجوز
            // والمدفوع/المشحون/المسلَّم مؤكَّد؛ الملغى بلا سجلّ (لم يعد يستهلك شيئاً). كوبون حُذف قديماً (الحذف كان فعلياً) لا
            // سجلّ لطلباته. الرمز فريد لكل متجر فالربط لا يكرّر طلباً.
            migrationBuilder.Sql("""
                INSERT INTO [CouponRedemptions]
                    ([TenantId], [CouponId], [OrderId], [CustomerId], [DiscountAmount], [Currency], [Status], [CreatedAt])
                SELECT o.[TenantId], c.[Id], o.[Id], o.[CustomerId],
                       ISNULL(o.[DiscountAmount], 0), ISNULL(o.[DiscountCurrency], o.[Currency]),
                       CASE WHEN o.[Status] = 0 THEN 0 ELSE 1 END, o.[CreatedAt]
                FROM [Orders] o
                JOIN [Coupons] c ON c.[TenantId] = o.[TenantId] AND c.[Code] = o.[CouponCode]
                WHERE o.[CouponCode] IS NOT NULL AND o.[Status] <> 4;
                """);

            // العدّاد القديم احتسب الاستخدام عند الدفع؛ الجديد عند إنشاء الطلب. فالمعلّق يُضاف إليه الآن — وإلا فدفعه بعد
            // الترحيل لا يُحتسب أبداً. القيمة القديمة لا تُمسّ (قد تعدّ مدفوعاً أُلغي لاحقاً — العدّ الأعلى هو الآمن).
            migrationBuilder.Sql("""
                UPDATE c SET c.[UsedCount] = c.[UsedCount] + r.[Pending]
                FROM [Coupons] c
                JOIN (SELECT [TenantId], [CouponId], COUNT(*) AS [Pending] FROM [CouponRedemptions]
                      WHERE [Status] = 0 GROUP BY [TenantId], [CouponId]) r
                  ON r.[TenantId] = c.[TenantId] AND r.[CouponId] = c.[Id];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // عكس الاحتساب: النموذج القديم يعدّ الاستخدام عند الدفع، فالمحجوز لطلب غير مدفوع يُطرح قبل حذف السجلّات.
            migrationBuilder.Sql("""
                UPDATE c SET c.[UsedCount] = CASE WHEN c.[UsedCount] > r.[Reserved] THEN c.[UsedCount] - r.[Reserved] ELSE 0 END
                FROM [Coupons] c
                JOIN (SELECT [TenantId], [CouponId], COUNT(*) AS [Reserved] FROM [CouponRedemptions]
                      WHERE [Status] = 0 GROUP BY [TenantId], [CouponId]) r
                  ON r.[TenantId] = c.[TenantId] AND r.[CouponId] = c.[Id];
                """);

            migrationBuilder.DropTable(
                name: "CouponRedemptions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Coupons_TenantId_Id",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "MaxUsesPerCustomer",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "Coupons");
        }
    }
}
