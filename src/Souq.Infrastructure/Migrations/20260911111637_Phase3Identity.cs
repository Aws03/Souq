using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    // ============================================================================
    // Phase 3 — فصل الهوية عن ملف الشراء (ADR-0010، D-06). البيانات تُنقل ولا تُحذف:
    //   1) Users (حسابات المتاجر والمنصّة) وRefreshTokens.
    //   2) كل صف عميل يصبح حساب دخول بالمعرّف نفسه (IDENTITY_INSERT) والمتجر نفسه والبريد نفسه
    //      وتجزئة كلمة المرور نفسها — الدخول القائم يعمل كما هو. الدور 'Admin' ⇒ TenantAdmin، وغيره
    //      ⇒ Customer. ختم أمان جديد لكل حساب (التوكنات القديمة بلا sstamp تسقط أصلاً).
    //   3) Customers.UserId = Id، ثم بلا قيمة افتراضية.
    //   4) عندها فقط تُحذف أعمدة الاعتماد من Customers (نُسخت في الخطوة 2).
    // الترتيب الذي ولّده EF كان يحذف الأعمدة أولاً — أي فقدان كل كلمات المرور؛ أُعيد ترتيبه يدوياً
    // ومُجرَّب على بيانات بشكل ما قبل المرحلة (MigrationRehearsalTests).
    // ============================================================================
    public partial class Phase3Identity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) جداول الهوية.
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmailConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmailVerificationTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EmailVerificationTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    BelongsToPlatform = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // (2) نقل الاعتماد: كل عميل حساب دخول بالمعرّف نفسه.
            migrationBuilder.Sql("""
                SET IDENTITY_INSERT [Users] ON;
                INSERT INTO [Users] ([Id], [TenantId], [Email], [NormalizedEmail], [FullName], [PasswordHash], [Role], [Status],
                                     [SecurityStamp], [FailedLoginCount], [PasswordResetTokenHash], [PasswordResetTokenExpiry],
                                     [CreatedAt], [UpdatedAt])
                SELECT [Id], [TenantId], [Email], UPPER(LTRIM(RTRIM([Email]))), [FullName], [PasswordHash],
                       CASE [Role] WHEN N'Admin' THEN N'TenantAdmin' ELSE N'Customer' END, 0,
                       REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 0,
                       [PasswordResetTokenHash], [PasswordResetTokenExpiry], [CreatedAt], [UpdatedAt]
                FROM [Customers];
                SET IDENTITY_INSERT [Users] OFF;
                """);

            // (3) ملف الشراء يشير لحسابه.
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);
            migrationBuilder.Sql("UPDATE [Customers] SET [UserId] = [Id];");
            migrationBuilder.Sql("""
                DECLARE @constraint sysname = (
                    SELECT d.[name] FROM sys.default_constraints d
                    JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                    WHERE d.[parent_object_id] = OBJECT_ID(N'[Customers]') AND c.[name] = N'UserId');
                IF @constraint IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT [' + @constraint + N']');
                """);

            // (4) أعمدة الاعتماد القديمة — بعد نسخها.
            migrationBuilder.DropIndex(name: "IX_Customers_PasswordResetTokenHash", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_TenantId_Email", table: "Customers");
            migrationBuilder.DropColumn(name: "PasswordHash", table: "Customers");
            migrationBuilder.DropColumn(name: "PasswordResetTokenExpiry", table: "Customers");
            migrationBuilder.DropColumn(name: "PasswordResetTokenHash", table: "Customers");
            migrationBuilder.DropColumn(name: "Role", table: "Customers");

            // (5) الفهارس والمفاتيح.
            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_UserId",
                table: "Customers",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_UserId",
                table: "Customers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TenantId",
                table: "RefreshTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailVerificationTokenHash",
                table: "Users",
                column: "EmailVerificationTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail_Platform",
                table: "Users",
                column: "NormalizedEmail",
                unique: true,
                filter: "[TenantId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PasswordResetTokenHash",
                table: "Users",
                column: "PasswordResetTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_NormalizedEmail",
                table: "Users",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Users_UserId",
                table: "Customers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // يعيد الاعتماد إلى Customers من حسابات ملفّاتهم. حسابات بلا ملف شراء (موظّفون، مالك المنصّة)
            // لا مكان لها في المخطّط القديم فتُفقد — Down للتطوير فقط، لا لبيانات إنتاج.
            migrationBuilder.DropForeignKey(name: "FK_Customers_Users_UserId", table: "Customers");

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Customers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiry",
                table: "Customers",
                type: "datetime2",
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "Customers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
            migrationBuilder.Sql("""
                UPDATE c SET c.[PasswordHash] = u.[PasswordHash],
                             c.[Role] = CASE WHEN u.[Role] IN (N'TenantAdmin', N'TenantStaff') THEN N'Admin' ELSE N'Customer' END,
                             c.[PasswordResetTokenHash] = u.[PasswordResetTokenHash],
                             c.[PasswordResetTokenExpiry] = u.[PasswordResetTokenExpiry]
                FROM [Customers] c JOIN [Users] u ON u.[Id] = c.[UserId];
                """);

            migrationBuilder.DropIndex(name: "IX_Customers_TenantId_Email", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_TenantId_UserId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_UserId", table: "Customers");
            migrationBuilder.DropColumn(name: "UserId", table: "Customers");

            migrationBuilder.DropTable(name: "RefreshTokens");
            migrationBuilder.DropTable(name: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PasswordResetTokenHash",
                table: "Customers",
                column: "PasswordResetTokenHash");
            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers",
                columns: new[] { "TenantId", "Email" },
                unique: true);
        }
    }
}
