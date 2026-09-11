using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // Phase 5 — الكتالوج (D-10، D-21): النصوص إلى جداول ترجمة، السعر إلى متغيّر افتراضي، الصورة إلى معرض، وIsActive
    // إلى حالة. البيانات تُنقل قبل حذف أعمدتها — الترتيب الذي ولّده EF كان يحذف أولاً (كل الأسماء والأسعار تضيع)؛
    // أُعيد ترتيبه يدوياً ومُجرَّب على بيانات بشكل ما قبل المرحلة (MigrationRehearsalTests):
    //   • ترجمات المنتج: ar من NameAr/Description، وen من NameEn حين يختلف (كان NameEn = NameAr إن لم يُترجم).
    //   • متغيّر افتراضي لكل منتج بسعره وعملته.
    //   • الصورة: رابط حقيقي فقط (/uploads أو http) — مفاتيح البذر القديمة ("headphones") لم تكن صوراً أصلاً.
    //   • IsActive ⇒ Active، وإلا Archived. Slug مؤقّت فريد "p-{Id}" يعدّله المدير.
    //   • ترجمة الفئة بلغة متجرها الافتراضية.
    // ============================================================================
    public partial class Phase5Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Products_TenantId_IsActive", table: "Products");

            // (1) الأعمدة الجديدة — قيم مؤقّتة تُملأ في (3) ثم تُزال قيودها الافتراضية في (4).
            migrationBuilder.AddColumn<string>(
                name: "Brand", table: "Products", type: "nvarchar(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "Slug", table: "Products", type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<int>(
                name: "Status", table: "Products", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(
                name: "IsActive", table: "Categories", type: "bit", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<int>(
                name: "SortOrder", table: "Categories", type: "int", nullable: false, defaultValue: 0);

            // (2) جداول الأبناء.
            migrationBuilder.CreateTable(
                name: "CategoryTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MetaTitle = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryTranslations_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategoryTranslations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductImages_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MetaTitle = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductTranslations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductTranslations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CompareAtPrice = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.UniqueConstraint("AK_ProductVariants_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // (3) نقل البيانات — قبل حذف أي عمود قديم.
            migrationBuilder.Sql("""
                INSERT INTO [ProductTranslations] ([TenantId], [ProductId], [Culture], [Name], [Description], [CreatedAt])
                SELECT [TenantId], [Id], N'ar', [NameAr], NULLIF(LTRIM(RTRIM([Description])), N''), [CreatedAt] FROM [Products];

                INSERT INTO [ProductTranslations] ([TenantId], [ProductId], [Culture], [Name], [CreatedAt])
                SELECT [TenantId], [Id], N'en', [NameEn], [CreatedAt] FROM [Products]
                WHERE [NameEn] IS NOT NULL AND LTRIM(RTRIM([NameEn])) <> N'' AND [NameEn] <> [NameAr];

                INSERT INTO [ProductVariants] ([TenantId], [ProductId], [Sku], [Price], [Currency], [CompareAtPrice], [IsDefault], [CreatedAt])
                SELECT [TenantId], [Id], NULL, [Price], [Currency], NULL, 1, [CreatedAt] FROM [Products];

                INSERT INTO [ProductImages] ([TenantId], [ProductId], [Url], [SortOrder], [CreatedAt])
                SELECT [TenantId], [Id], [ImageUrl], 0, [CreatedAt] FROM [Products]
                WHERE [ImageUrl] LIKE N'/uploads/%' OR [ImageUrl] LIKE N'http://%' OR [ImageUrl] LIKE N'https://%';

                UPDATE [Products] SET [Status] = CASE WHEN [IsActive] = 1 THEN 1 ELSE 2 END,
                                      [Slug] = CONCAT(N'p-', [Id]);

                INSERT INTO [CategoryTranslations] ([TenantId], [CategoryId], [Culture], [Name], [CreatedAt])
                SELECT c.[TenantId], c.[Id], t.[DefaultCulture], c.[Name], c.[CreatedAt]
                FROM [Categories] c JOIN [Tenants] t ON t.[Id] = c.[TenantId];
                """);

            // (4) لا قيم افتراضية مخترعة: صف جديد بلا قيمة صريحة يُرفض بدل أن يأخذ "" أو مسودّة بصمت.
            DropDefault(migrationBuilder, "Products", "Slug");
            DropDefault(migrationBuilder, "Products", "Status");
            DropDefault(migrationBuilder, "Categories", "IsActive");
            DropDefault(migrationBuilder, "Categories", "SortOrder");

            // (5) الأعمدة القديمة — بعد نسخها.
            migrationBuilder.DropColumn(name: "Currency", table: "Products");
            migrationBuilder.DropColumn(name: "Description", table: "Products");
            migrationBuilder.DropColumn(name: "ImageUrl", table: "Products");
            migrationBuilder.DropColumn(name: "IsActive", table: "Products");
            migrationBuilder.DropColumn(name: "NameAr", table: "Products");
            migrationBuilder.DropColumn(name: "NameEn", table: "Products");
            migrationBuilder.DropColumn(name: "Price", table: "Products");
            migrationBuilder.DropColumn(name: "Name", table: "Categories");

            // (6) الفهارس — بعد تعبئة المعرّفات الفريدة.
            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Slug",
                table: "Products",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Status_CategoryId",
                table: "Products",
                columns: new[] { "TenantId", "Status", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId_ParentId_SortOrder",
                table: "Categories",
                columns: new[] { "TenantId", "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTranslations_CategoryId_Culture",
                table: "CategoryTranslations",
                columns: new[] { "CategoryId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTranslations_TenantId",
                table: "CategoryTranslations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId_SortOrder",
                table: "ProductImages",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_TenantId",
                table: "ProductImages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTranslations_ProductId_Culture",
                table: "ProductTranslations",
                columns: new[] { "ProductId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductTranslations_TenantId",
                table: "ProductTranslations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_Default",
                table: "ProductVariants",
                column: "ProductId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_IsDefault",
                table: "ProductVariants",
                columns: new[] { "ProductId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_Sku",
                table: "ProductVariants",
                columns: new[] { "TenantId", "Sku" },
                unique: true,
                filter: "[Sku] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // يعيد الأعمدة القديمة من الأبناء: الترجمة العربية (ثم أي لغة)، سعر المتغيّر الافتراضي، أول صورة، الحالة.
            // ما لا مكان له في المخطّط القديم يُفقد (لغات أخرى، صور بعد الأولى، SKU، سعر المقارنة، مسودّة ≠ مؤرشف) —
            // Down للتطوير، لا لبيانات إنتاج.
            migrationBuilder.DropIndex(name: "IX_Products_TenantId_Slug", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Products_TenantId_Status_CategoryId", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Categories_TenantId_ParentId_SortOrder", table: "Categories");

            migrationBuilder.AddColumn<string>(
                name: "Currency", table: "Products", type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "Description", table: "Products", type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl", table: "Products", type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<bool>(
                name: "IsActive", table: "Products", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(
                name: "NameAr", table: "Products", type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "NameEn", table: "Products", type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<decimal>(
                name: "Price", table: "Products", type: "decimal(19,4)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<string>(
                name: "Name", table: "Categories", type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE p SET
                    [NameAr] = COALESCE((SELECT TOP 1 t.[Name] FROM [ProductTranslations] t WHERE t.[ProductId] = p.[Id]
                                         ORDER BY CASE t.[Culture] WHEN N'ar' THEN 0 ELSE 1 END, t.[Culture]), p.[Slug]),
                    [NameEn] = COALESCE((SELECT TOP 1 t.[Name] FROM [ProductTranslations] t WHERE t.[ProductId] = p.[Id]
                                         ORDER BY CASE t.[Culture] WHEN N'en' THEN 0 ELSE 1 END, t.[Culture]), p.[Slug]),
                    [Description] = COALESCE((SELECT TOP 1 LEFT(t.[Description], 2000) FROM [ProductTranslations] t
                                              WHERE t.[ProductId] = p.[Id] AND t.[Description] IS NOT NULL
                                              ORDER BY CASE t.[Culture] WHEN N'ar' THEN 0 ELSE 1 END, t.[Culture]), N''),
                    [Price] = v.[Price], [Currency] = v.[Currency],
                    [ImageUrl] = COALESCE((SELECT TOP 1 i.[Url] FROM [ProductImages] i WHERE i.[ProductId] = p.[Id]
                                           ORDER BY i.[SortOrder], i.[Id]), N''),
                    [IsActive] = CASE WHEN p.[Status] = 1 THEN 1 ELSE 0 END
                FROM [Products] p JOIN [ProductVariants] v ON v.[ProductId] = p.[Id] AND v.[IsDefault] = 1;

                UPDATE c SET [Name] = COALESCE((SELECT TOP 1 LEFT(t.[Name], 100) FROM [CategoryTranslations] t
                                                JOIN [Tenants] tn ON tn.[Id] = c.[TenantId]
                                                WHERE t.[CategoryId] = c.[Id]
                                                ORDER BY CASE WHEN t.[Culture] = tn.[DefaultCulture] THEN 0 ELSE 1 END, t.[Culture]), c.[Slug])
                FROM [Categories] c;
                """);

            migrationBuilder.DropTable(name: "CategoryTranslations");
            migrationBuilder.DropTable(name: "ProductImages");
            migrationBuilder.DropTable(name: "ProductTranslations");
            migrationBuilder.DropTable(name: "ProductVariants");

            migrationBuilder.DropColumn(name: "Brand", table: "Products");
            migrationBuilder.DropColumn(name: "Slug", table: "Products");
            migrationBuilder.DropColumn(name: "Status", table: "Products");
            migrationBuilder.DropColumn(name: "IsActive", table: "Categories");
            migrationBuilder.DropColumn(name: "SortOrder", table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_IsActive",
                table: "Products",
                columns: new[] { "TenantId", "IsActive" });
        }

        private static void DropDefault(MigrationBuilder migrationBuilder, string table, string column) =>
            migrationBuilder.Sql($"""
                DECLARE @constraint sysname = (
                    SELECT d.[name] FROM sys.default_constraints d
                    JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                    WHERE d.[parent_object_id] = OBJECT_ID(N'[{table}]') AND c.[name] = N'{column}');
                IF @constraint IS NOT NULL EXEC(N'ALTER TABLE [{table}] DROP CONSTRAINT [' + @constraint + N']');
                """);
    }
}
