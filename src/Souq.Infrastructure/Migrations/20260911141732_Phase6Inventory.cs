using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // المرحلة 6 (ADR-0026): المخزون ينتقل من Products إلى InventoryItems (لكل متغيّر: موجود/محجوز) مع حجوزات صريحة.
    // أعاد EF توليد الحذف أولاً (StockQuantity قبل أي نسخ ⇒ يضيع كل مخزون) — الترتيب هنا يدوي حافظ للبيانات:
    //   1) الجداول الجديدة وعمود InventoryItemId في الحركات (يقبل null مؤقتاً).
    //   2) نسخ: مخزون لكل متغيّر افتراضي (الموجود = القديم + ما تحجزه الطلبات المعلّقة، لأن الطلب كان يُنقصه فوراً)،
    //      حجوزات نشطة للمعلّقة وملتزمة للمدفوعة غير المشحونة (فيعيد إلغاؤها مخزونها كما قبل)، نسب الحركات لمخزونها،
    //      ورصيد افتتاحي حيث لا يطابق مجموعُ السجلّ الموجودَ — من هنا Σ الحركات = الموجود لكل مخزون.
    //   3) القيود والفهارس، ثم حذف أعمدة Products القديمة بعد النسخ.
    // Down يعيد المخزون المتاح إلى Products — للتطوير (الحجوزات لا مكان لها في المخطّط القديم).
    // ============================================================================
    /// <inheritdoc />
    public partial class Phase6Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    OnHand = table.Column<int>(type: "int", nullable: false),
                    Reserved = table.Column<int>(type: "int", nullable: false),
                    LowStockThreshold = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.UniqueConstraint("AK_InventoryItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_InventoryItems_Quantities", "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]");
                    table.CheckConstraint("CK_InventoryItems_Threshold", "[LowStockThreshold] >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryItems_ProductVariants_TenantId_VariantId",
                        columns: x => new { x.TenantId, x.VariantId },
                        principalTable: "ProductVariants",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItems_Products_TenantId_ProductId",
                        columns: x => new { x.TenantId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    InventoryItemId = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.CheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_StockReservations_InventoryItems_TenantId_InventoryItemId",
                        columns: x => new { x.TenantId, x.InventoryItemId },
                        principalTable: "InventoryItems",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<int>(
                name: "InventoryItemId", table: "StockMovements", type: "int", nullable: true);

            migrationBuilder.Sql("""
                -- (أ) مخزون لكل متغيّر افتراضي. الطلب المعلّق كان يُنقص StockQuantity فوراً؛ الآن يحجز من الموجود.
                INSERT INTO [InventoryItems] ([TenantId], [ProductId], [VariantId], [OnHand], [Reserved], [LowStockThreshold], [CreatedAt])
                SELECT p.[TenantId], p.[Id], v.[Id],
                       p.[StockQuantity] + ISNULL(pending.[Quantity], 0), ISNULL(pending.[Quantity], 0),
                       p.[LowStockThreshold], SYSUTCDATETIME()
                FROM [Products] p
                JOIN [ProductVariants] v ON v.[ProductId] = p.[Id] AND v.[IsDefault] = 1
                OUTER APPLY (SELECT SUM(oi.[Quantity]) AS [Quantity]
                             FROM [OrderItems] oi JOIN [Orders] o ON o.[Id] = oi.[OrderId]
                             WHERE oi.[ProductId] = p.[Id] AND o.[Status] = 0) pending;

                -- (ب) حجوزات صريحة: نشطة للمعلّقة (مهلة 30 دقيقة من الآن ثم يلتقطها منسّق الانتهاء)، وملتزمة للمدفوعة غير
                --     المشحونة كي يعيد إلغاؤها مخزونها.
                INSERT INTO [StockReservations] ([TenantId], [InventoryItemId], [Reference], [Quantity], [Status], [ExpiresAt], [ClosedAt], [CreatedAt])
                SELECT o.[TenantId], i.[Id], CONCAT(N'order:', o.[Id]), SUM(oi.[Quantity]),
                       CASE o.[Status] WHEN 0 THEN 0 ELSE 1 END,
                       DATEADD(MINUTE, 30, SYSUTCDATETIME()),
                       CASE o.[Status] WHEN 0 THEN NULL ELSE SYSUTCDATETIME() END,
                       SYSUTCDATETIME()
                FROM [Orders] o
                JOIN [OrderItems] oi ON oi.[OrderId] = o.[Id]
                JOIN [InventoryItems] i ON i.[ProductId] = oi.[ProductId]
                WHERE o.[Status] IN (0, 1)
                GROUP BY o.[TenantId], i.[Id], o.[Id], o.[Status];

                -- (ج) كل حركة سابقة تُنسب لمخزون منتجها.
                UPDATE m SET m.[InventoryItemId] = i.[Id]
                FROM [StockMovements] m JOIN [InventoryItems] i ON i.[ProductId] = m.[ProductId];

                -- (د) رصيد افتتاحي حيث لا يطابق مجموعُ السجلّ الموجودَ (مخزون بُذر بلا حركة، بيع معلّق صار حجزاً).
                INSERT INTO [StockMovements] ([TenantId], [ProductId], [InventoryItemId], [Type], [QuantityChange], [NewQuantity], [Note], [CreatedAt])
                SELECT i.[TenantId], i.[ProductId], i.[Id], 2, i.[OnHand] - ISNULL(ledger.[Total], 0), i.[OnHand],
                       N'رصيد افتتاحي — ترحيل المرحلة 6', SYSUTCDATETIME()
                FROM [InventoryItems] i
                OUTER APPLY (SELECT SUM(m.[QuantityChange]) AS [Total] FROM [StockMovements] m WHERE m.[InventoryItemId] = i.[Id]) ledger
                WHERE i.[OnHand] <> ISNULL(ledger.[Total], 0);
                """);

            migrationBuilder.AlterColumn<int>(
                name: "InventoryItemId", table: "StockMovements", type: "int", nullable: false,
                oldClrType: typeof(int), oldType: "int", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_InventoryItemId_CreatedAt",
                table: "StockMovements",
                columns: new[] { "InventoryItemId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId_InventoryItemId",
                table: "StockMovements",
                columns: new[] { "TenantId", "InventoryItemId" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_InventoryItems_TenantId_InventoryItemId",
                table: "StockMovements",
                columns: new[] { "TenantId", "InventoryItemId" },
                principalTable: "InventoryItems",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_TenantId_ProductId",
                table: "InventoryItems",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_TenantId_VariantId",
                table: "InventoryItems",
                columns: new[] { "TenantId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_TenantId_ExpiresAt",
                table: "StockReservations",
                columns: new[] { "TenantId", "ExpiresAt" },
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_TenantId_InventoryItemId",
                table: "StockReservations",
                columns: new[] { "TenantId", "InventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_TenantId_Reference",
                table: "StockReservations",
                columns: new[] { "TenantId", "Reference" });

            // بعد النسخ فقط.
            migrationBuilder.DropColumn(name: "LowStockThreshold", table: "Products");
            migrationBuilder.DropColumn(name: "StockQuantity", table: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LowStockThreshold", table: "Products", type: "int", nullable: false, defaultValue: 5);
            migrationBuilder.AddColumn<int>(
                name: "StockQuantity", table: "Products", type: "int", nullable: false, defaultValue: 0);

            // المخطّط القديم يعرف "المخزون القابل للبيع" فقط: المتاح (الموجود − المحجوز)، كما كان الطلب المعلّق يُنقصه.
            migrationBuilder.Sql("""
                UPDATE p SET p.[StockQuantity] = i.[OnHand] - i.[Reserved], p.[LowStockThreshold] = i.[LowStockThreshold]
                FROM [Products] p
                JOIN [InventoryItems] i ON i.[ProductId] = p.[Id]
                JOIN [ProductVariants] v ON v.[Id] = i.[VariantId] AND v.[IsDefault] = 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_InventoryItems_TenantId_InventoryItemId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_InventoryItemId_CreatedAt",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_TenantId_InventoryItemId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockReservations");

            migrationBuilder.DropTable(
                name: "InventoryItems");
        }
    }
}
