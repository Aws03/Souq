using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderLinesRecordVariant : Migration
    {
        private static void DropDefaultConstraint(MigrationBuilder migrationBuilder, string table, string column) =>
            migrationBuilder.Sql($"""
                DECLARE @constraint sysname = (
                    SELECT d.[name] FROM sys.default_constraints d
                    JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                    WHERE d.[parent_object_id] = OBJECT_ID(N'[{table}]') AND c.[name] = N'{column}');
                IF @constraint IS NOT NULL EXEC(N'ALTER TABLE [{table}] DROP CONSTRAINT [' + @constraint + N']');
                """);

        // ====================================================================================================================
        // أسطر الطلب تسجّل المتغيّر المشترى (ProductVariants.md V1، ADR-0039). إضافية بالكامل، والنقل مكتوب يدوياً:
        //   • ProductVariants.IsActive: كل متغيّر قائم نشط (true لا false التي ولّدها EF — false كانت ستُخفي الكتالوج كله).
        //   • OrderItems.VariantId يُملأ من المتغيّر الافتراضي لمنتج السطر. الربط يقيني لا تخمين: منذ Phase5Catalog لم ينشئ
        //     متغيّراً إلا مُنشئ Product (افتراضياً)، فلكل منتج متغيّر واحد طوال عمره وهو ما بيع. حارسان يوقفان الهجرة (وتُلغى
        //     معاملتها كلها) بدل ربط خاطئ: متغيّر غير افتراضي موجود، أو سطر بقي بلا متغيّر.
        //   • VariantLabel وSku يبقيان null للأسطر القائمة: SKU اليوم قد يختلف عمّا كان لحظة البيع، والوصف لم يوجد — لا
        //     نملأ الفاتورة التاريخية بقيم مختلقة.
        //   • سطر واحد لكل متغيّر في الطلب: الدمج كان بالمنتج منذ أول نسخة، فلا تكرار قائم — وحارس ثالث يوقف الهجرة إن وُجد.
        // ====================================================================================================================
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ProductVariants] WHERE [IsDefault] = 0)
                    THROW 50001, N'OrderLinesRecordVariant: an existing product has a non-default variant, so historical order lines cannot be linked to their variant with certainty. Stop and decide the mapping by hand.', 1;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProductVariants",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_DefaultIsActive",
                table: "ProductVariants",
                sql: "[IsDefault] = 0 OR [IsActive] = 1");

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "OrderItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VariantLabel",
                table: "OrderItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE i SET [VariantId] = v.[Id]
                FROM [OrderItems] i
                JOIN [ProductVariants] v
                  ON v.[TenantId] = i.[TenantId] AND v.[ProductId] = i.[ProductId] AND v.[IsDefault] = 1;

                IF EXISTS (SELECT 1 FROM [OrderItems] WHERE [VariantId] = 0)
                    THROW 50002, N'OrderLinesRecordVariant: an order line has no default variant for its product in the same store.', 1;

                IF EXISTS (SELECT 1 FROM [OrderItems] GROUP BY [OrderId], [VariantId] HAVING COUNT(*) > 1)
                    THROW 50003, N'OrderLinesRecordVariant: an order has two lines for the same product; merge them by hand before this migration.', 1;
                """);

            // القيمة الافتراضية 0 كانت للملء وحده: سطر جديد بلا متغيّر صريح يُرفض بصوت عالٍ لا أن يأخذ 0 بصمت.
            DropDefaultConstraint(migrationBuilder, "OrderItems", "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId_VariantId",
                table: "OrderItems",
                columns: new[] { "OrderId", "VariantId" },
                unique: true);

            // الفهرس الفريد أعلاه يبدأ بـ OrderId فيغطّي مفتاح الطلب الأجنبي.
            migrationBuilder.DropIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_TenantId_VariantId",
                table: "OrderItems",
                columns: new[] { "TenantId", "VariantId" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_ProductVariants_TenantId_VariantId",
                table: "OrderItems",
                columns: new[] { "TenantId", "VariantId" },
                principalTable: "ProductVariants",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_ProductVariants_TenantId_VariantId",
                table: "OrderItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_DefaultIsActive",
                table: "ProductVariants");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_OrderId_VariantId",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_TenantId_VariantId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "VariantLabel",
                table: "OrderItems");
        }
    }
}
