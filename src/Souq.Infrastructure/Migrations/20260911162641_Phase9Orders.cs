using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase9Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChangedBy",
                table: "OrderStatusHistories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChangedByUserId",
                table: "OrderStatusHistories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddress",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OrderNumber",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlacedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlacedSubtotal",
                table: "Orders",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlacedTotal",
                table: "Orders",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TrackingToken",
                table: "Orders",
                type: "char(32)",
                unicode: false,
                fixedLength: true,
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // المرحلة 9 — الطلبات القائمة قبل الفهارس الفريدة، بلا حذف ولا تعديل لما سواها:
            //   الرقم 1001… لكل متجر بترتيب الإنشاء؛ رمز تتبّع عشوائي لكل صف (NEWID)؛ الفوترة = الشحن (اللقطة الوحيدة
            //   المعروفة)؛ التثبيت لحظة الإنشاء بإجماليات الأسطر والخصم — كما يحسبها Order بالضبط. ثم تُزال القيم
            //   الافتراضية المؤقّتة: رقم الطلب ورمزه وعنوانه لا تُترك لقيمة افتراضية تخفي نسيانها.
            migrationBuilder.Sql("""
                UPDATE o SET [OrderNumber] = n.[Number]
                FROM [Orders] o
                JOIN (SELECT [Id], 1000 + ROW_NUMBER() OVER (PARTITION BY [TenantId] ORDER BY [CreatedAt], [Id]) AS [Number]
                      FROM [Orders]) n ON n.[Id] = o.[Id];

                UPDATE [Orders] SET
                    [TrackingToken] = LOWER(REPLACE(CONVERT(char(36), NEWID()), '-', '')),
                    [BillingAddress] = [ShippingAddress],
                    [PlacedAt] = [CreatedAt],
                    [PlacedSubtotal] = ISNULL((SELECT SUM(i.[UnitPrice] * i.[Quantity]) FROM [OrderItems] i WHERE i.[OrderId] = [Orders].[Id]), 0),
                    [PlacedTotal] = ISNULL((SELECT SUM(i.[UnitPrice] * i.[Quantity]) FROM [OrderItems] i WHERE i.[OrderId] = [Orders].[Id]), 0)
                                    - ISNULL([DiscountAmount], 0);

                DECLARE @dropDefaults nvarchar(max) = N'';
                SELECT @dropDefaults += N'ALTER TABLE [Orders] DROP CONSTRAINT ' + QUOTENAME(d.[name]) + N';'
                FROM sys.default_constraints d
                JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                WHERE d.[parent_object_id] = OBJECT_ID(N'[Orders]') AND c.[name] IN (N'OrderNumber', N'TrackingToken', N'BillingAddress');
                EXEC sp_executesql @dropDefaults;
                """);

            migrationBuilder.CreateTable(
                name: "OrderNumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderNumberSequences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // عدّاد كل متجر عند آخر رقم صدر فيه؛ متجر بلا طلبات يُنشأ صفّه مع أول طلب (OrderNumbers).
            migrationBuilder.Sql("""
                INSERT INTO [OrderNumberSequences] ([TenantId], [LastNumber], [CreatedAt])
                SELECT [TenantId], MAX([OrderNumber]), SYSUTCDATETIME() FROM [Orders] GROUP BY [TenantId];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_OrderNumber",
                table: "Orders",
                columns: new[] { "TenantId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Status_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_TrackingToken",
                table: "Orders",
                columns: new[] { "TenantId", "TrackingToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderNumberSequences_TenantId",
                table: "OrderNumberSequences",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderNumberSequences");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_OrderNumber",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_Status_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_TrackingToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ChangedBy",
                table: "OrderStatusHistories");

            migrationBuilder.DropColumn(
                name: "ChangedByUserId",
                table: "OrderStatusHistories");

            migrationBuilder.DropColumn(
                name: "BillingAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PlacedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PlacedSubtotal",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PlacedTotal",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrackingToken",
                table: "Orders");
        }
    }
}
