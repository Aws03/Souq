using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase13ReviewsWishlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_TenantId_ProductId",
                table: "Reviews");

            migrationBuilder.AddColumn<bool>(
                name: "ReviewsAutoApprove",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // مكتوب يدوياً (ADR-0033): المتاجر القائمة كانت تنشر كل تقييم فوراً — تبقى كذلك حتى يغيّر صاحبها السياسة. المتجر
            // الجديد بعد هذه الهجرة يبدأ بالإشراف (Tenant.ReviewsAutoApprove = false).
            migrationBuilder.Sql("UPDATE [Tenants] SET [ReviewsAutoApprove] = 1;");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModeratedAt",
                table: "Reviews",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModeratedByUserId",
                table: "Reviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationNote",
                table: "Reviews",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Reviews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // مكتوب يدوياً: كل تقييم قائم كان معروضاً ⇒ معتمد (1). بلا مشرف ولا وقت قرار — لم يكن قراراً، بل سياسة ما قبل الإشراف.
            migrationBuilder.Sql("UPDATE [Reviews] SET [Status] = 1;");

            migrationBuilder.CreateTable(
                name: "WishlistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WishlistItems_Customers_TenantId_CustomerId",
                        columns: x => new { x.TenantId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WishlistItems_Products_TenantId_ProductId",
                        columns: x => new { x.TenantId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WishlistItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_TenantId_ProductId_Status",
                table: "Reviews",
                columns: new[] { "TenantId", "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_TenantId_Status_CreatedAt",
                table: "Reviews",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_TenantId_CustomerId_ProductId",
                table: "WishlistItems",
                columns: new[] { "TenantId", "CustomerId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_TenantId_ProductId",
                table: "WishlistItems",
                columns: new[] { "TenantId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WishlistItems");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_TenantId_ProductId_Status",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_TenantId_Status_CreatedAt",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ReviewsAutoApprove",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ModeratedAt",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ModeratedByUserId",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ModerationNote",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_TenantId_ProductId",
                table: "Reviews",
                columns: new[] { "TenantId", "ProductId" });
        }
    }
}
