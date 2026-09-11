using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // Phase 2 — تعدّد المستأجرين (ADR-0005). هجرة إضافية غير هدّامة: لا يُحذف صف ولا عمود بيانات.
    //   1) جداول المنصّة (Tenants/TenantDomains) + المتجر الافتراضي بمعرّف ثابت 1 ("Marka Demo"، P-04).
    //   2) TenantId على كل جدول متجر: عمود بقيمة افتراضية 1 يملأ كل الصفوف القائمة دفعة واحدة
    //      (backfill ذرّي داخل معاملة الهجرة)، ثم يُزال القيد الافتراضي — صف جديد بلا متجر صريح يُرفض
    //      بدل أن يذهب بصمت للمتجر 1.
    //   3) عملة الطلب لقطة من عملة أسطره (JOD لطلب بلا أسطر — كانت العملة الوحيدة قبل هذه المرحلة).
    //   4) التفرّد لكل متجر: (TenantId, Slug)/(TenantId, Code)/(TenantId, Email) — أرخى من السابقة، فلا
    //      يمكن أن يفشل إنشاؤها على بيانات قائمة.
    //   5) المفاتيح الأجنبية بين بيانات المتاجر تحمل المتجر: (TenantId, XId) ⇒ (TenantId, Id) عبر مفاتيح
    //      بديلة — يستحيل حتى على مستوى القاعدة أن يشير صف متجر لصف متجر آخر. كل البيانات القائمة في
    //      متجر واحد، فكل مرجع قائم صالح بالتعريف.
    //   6) مفاتيح أجنبية إلى Tenants (Restrict) + فهارس تبدأ بـ TenantId.
    // مُجرَّبة على SQL Server حقيقي ببيانات ما قبل المرحلة (MigrationRehearsalTests).
    // Down آمن فقط ما دامت البيانات كلها في متجر واحد (الفهارس العامة القديمة قد تتعارض بعد ذلك).
    // ============================================================================
    public partial class Phase2MultiTenancy : Migration
    {
        private const int DefaultTenantId = 1;

        private static readonly string[] TenantOwnedTables =
        [
            "Categories", "Products", "Customers", "Orders", "OrderItems",
            "OrderStatusHistories", "Coupons", "Reviews", "StockMovements",
        ];

        // الأساس الذي تشير إليه مفاتيح المتجر المركّبة (TenantId, Id).
        private static readonly string[] TenantKeyPrincipals = ["Categories", "Products", "Customers", "Orders"];

        // (الجدول التابع، أعمدته، الجدول الأصل) لكل مرجع بين بيانات المتاجر.
        private static readonly (string Table, string Column, string Principal)[] TenantScopedReferences =
        [
            ("Products", "CategoryId", "Categories"),
            ("OrderItems", "ProductId", "Products"),
            ("Orders", "CustomerId", "Customers"),
            ("Reviews", "ProductId", "Products"),
            ("Reviews", "CustomerId", "Customers"),
            ("Reviews", "OrderId", "Orders"),
            ("StockMovements", "ProductId", "Products"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (0) المراجع والفهارس القديمة (بلا متجر) تُستبدل أدناه بنظيراتها المركّبة.
            foreach (var (table, column, principal) in TenantScopedReferences)
                migrationBuilder.DropForeignKey(name: $"FK_{table}_{principal}_{column}", table: table);
            migrationBuilder.DropIndex(name: "IX_Reviews_OrderId", table: "Reviews");
            migrationBuilder.DropIndex(name: "IX_Reviews_ProductId", table: "Reviews");
            migrationBuilder.DropIndex(name: "IX_Products_CategoryId", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Orders_CustomerId", table: "Orders");
            migrationBuilder.DropIndex(name: "IX_OrderItems_ProductId", table: "OrderItems");
            migrationBuilder.DropIndex(name: "IX_Customers_Email", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Coupons_Code", table: "Coupons");
            migrationBuilder.DropIndex(name: "IX_Categories_Slug", table: "Categories");

            // (1) جداول المنصّة + المتجر الافتراضي.
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DefaultCulture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantDomains_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql($"""
                SET IDENTITY_INSERT [Tenants] ON;
                INSERT INTO [Tenants] ([Id], [Name], [Slug], [Status], [DefaultCulture], [Currency], [TimeZone], [CreatedAt])
                VALUES ({DefaultTenantId}, N'Marka Demo', N'marka', 1, N'ar', N'JOD', N'Asia/Amman', SYSUTCDATETIME());
                SET IDENTITY_INSERT [Tenants] OFF;
                """);

            // (2) TenantId + backfill إلى المتجر الافتراضي، ثم بلا قيمة افتراضية.
            foreach (var table in TenantOwnedTables)
            {
                migrationBuilder.AddColumn<int>(
                    name: "TenantId",
                    table: table,
                    type: "int",
                    nullable: false,
                    defaultValue: DefaultTenantId);
                DropDefaultConstraint(migrationBuilder, table, "TenantId");
            }

            // (3) عملة الطلب.
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "JOD");
            migrationBuilder.Sql("""
                UPDATE o SET o.[Currency] = i.[Currency]
                FROM [Orders] o
                CROSS APPLY (SELECT TOP 1 [Currency] FROM [OrderItems] WHERE [OrderId] = o.[Id] ORDER BY [Id]) i
                WHERE i.[Currency] IS NOT NULL;
                """);
            DropDefaultConstraint(migrationBuilder, "Orders", "Currency");

            // (4) التفرّد لكل متجر + فهارس المنصّة.
            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_TenantId_Code",
                table: "Coupons",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId_Slug",
                table: "Categories",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_Host",
                table: "TenantDomains",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_TenantId",
                table: "TenantDomains",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            // (5) مراجع داخل المتجر: مفاتيح بديلة (TenantId, Id) ثم مفاتيح أجنبية مركّبة بفهارسها.
            foreach (var principal in TenantKeyPrincipals)
                migrationBuilder.AddUniqueConstraint(
                    name: $"AK_{principal}_TenantId_Id",
                    table: principal,
                    columns: new[] { "TenantId", "Id" });

            foreach (var (table, column, principal) in TenantScopedReferences)
            {
                migrationBuilder.CreateIndex(
                    name: $"IX_{table}_TenantId_{column}",
                    table: table,
                    columns: new[] { "TenantId", column });
                migrationBuilder.AddForeignKey(
                    name: $"FK_{table}_{principal}_TenantId_{column}",
                    table: table,
                    columns: new[] { "TenantId", column },
                    principalTable: principal,
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            }

            // (6) فهارس تبدأ بالمستأجر + مفاتيح أجنبية إلى Tenants.
            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_IsActive",
                table: "Products",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_CreatedAt",
                table: "Orders",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_TenantId",
                table: "OrderStatusHistories",
                column: "TenantId");

            foreach (var table in TenantOwnedTables)
                migrationBuilder.AddForeignKey(
                    name: $"FK_{table}_Tenants_TenantId",
                    table: table,
                    column: "TenantId",
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantOwnedTables)
                migrationBuilder.DropForeignKey(name: $"FK_{table}_Tenants_TenantId", table: table);

            foreach (var (table, column, principal) in TenantScopedReferences)
            {
                migrationBuilder.DropForeignKey(name: $"FK_{table}_{principal}_TenantId_{column}", table: table);
                migrationBuilder.DropIndex(name: $"IX_{table}_TenantId_{column}", table: table);
            }
            foreach (var principal in TenantKeyPrincipals)
                migrationBuilder.DropUniqueConstraint(name: $"AK_{principal}_TenantId_Id", table: principal);

            migrationBuilder.DropIndex(name: "IX_OrderStatusHistories_TenantId", table: "OrderStatusHistories");
            migrationBuilder.DropIndex(name: "IX_Products_TenantId_IsActive", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Orders_TenantId_CreatedAt", table: "Orders");
            migrationBuilder.DropIndex(name: "IX_Customers_TenantId_Email", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Coupons_TenantId_Code", table: "Coupons");
            migrationBuilder.DropIndex(name: "IX_Categories_TenantId_Slug", table: "Categories");

            foreach (var table in TenantOwnedTables)
                migrationBuilder.DropColumn(name: "TenantId", table: table);
            migrationBuilder.DropColumn(name: "Currency", table: "Orders");

            migrationBuilder.DropTable(name: "TenantDomains");
            migrationBuilder.DropTable(name: "Tenants");

            migrationBuilder.CreateIndex(name: "IX_Customers_Email", table: "Customers", column: "Email", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Coupons_Code", table: "Coupons", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Categories_Slug", table: "Categories", column: "Slug", unique: true);
            migrationBuilder.CreateIndex(name: "IX_OrderItems_ProductId", table: "OrderItems", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_Orders_CustomerId", table: "Orders", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_Products_CategoryId", table: "Products", column: "CategoryId");
            migrationBuilder.CreateIndex(name: "IX_Reviews_ProductId", table: "Reviews", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_Reviews_OrderId", table: "Reviews", column: "OrderId");

            foreach (var (table, column, principal) in TenantScopedReferences)
                migrationBuilder.AddForeignKey(
                    name: $"FK_{table}_{principal}_{column}",
                    table: table,
                    column: column,
                    principalTable: principal,
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
        }

        // يزيل قيد القيمة الافتراضية (اسمه يولّده SQL Server) بعد انتهاء دوره في ملء الصفوف القائمة.
        private static void DropDefaultConstraint(MigrationBuilder migrationBuilder, string table, string column) =>
            migrationBuilder.Sql($"""
                DECLARE @constraint sysname = (
                    SELECT d.[name] FROM sys.default_constraints d
                    JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                    WHERE d.[parent_object_id] = OBJECT_ID(N'[{table}]') AND c.[name] = N'{column}');
                IF @constraint IS NOT NULL EXEC(N'ALTER TABLE [{table}] DROP CONSTRAINT [' + @constraint + N']');
                """);
    }
}
