using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase11Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Gateway = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    PendingRefundAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.UniqueConstraint("AK_Payments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Payments_Orders_TenantId_OrderId",
                        columns: x => new { x.TenantId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorePaymentAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PublishableKey = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    LiveMode = table.Column<bool>(type: "bit", nullable: false),
                    SecretKeyCipher = table.Column<string>(type: "varchar(1024)", unicode: false, maxLength: 1024, nullable: false),
                    SecretKeyHint = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    WebhookSecretCipher = table.Column<string>(type: "varchar(1024)", unicode: false, maxLength: 1024, nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorePaymentAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorePaymentAccounts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProviderRefundId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Refunds_Payments_TenantId_PaymentId",
                        columns: x => new { x.TenantId, x.PaymentId },
                        principalTable: "Payments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_OrderId",
                table: "Payments",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_ProviderPaymentId",
                table: "Payments",
                columns: new[] { "TenantId", "ProviderPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId_PaymentId",
                table: "Refunds",
                columns: new[] { "TenantId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorePaymentAccounts_TenantId",
                table: "StorePaymentAccounts",
                column: "TenantId",
                unique: true);

            // نقل إضافي بلا حذف (ADR-0031): كل طلب له نيّة دفع يأخذ دفعته — بمبلغه المثبَّت (المرحلة 9) وعملته، والحساب الذي
            // أنشأها (نيّات البوّابة التجريبية pi_fake_، وإلا حساب النشر: لا حسابات متاجر قبل هذه المرحلة). الحالة من الطلب:
            // المعلّق معلّقة، المدفوع/المشحون/المسلَّم ناجحة، والملغى ناجحة إن شهد سجلّه أنه دُفع قبل إلغائه (الإلغاء قبل
            // المرحلة 11 لم يردّ المال — فتبقى الدفعة قابلة للاسترداد الآن)، وإلا ملغاة. بعد الفهارس الفريدة: تكرار نيّة بين
            // طلبين تلفٌ يوقف الترحيل صراحةً بدل إخفائه.
            migrationBuilder.Sql("""
                INSERT INTO [Payments] ([TenantId], [OrderId], [Gateway], [ProviderPaymentId], [Amount], [Currency], [Status],
                                        [RefundedAmount], [PendingRefundAmount], [CreatedAt])
                SELECT o.[TenantId], o.[Id],
                       CASE WHEN o.[PaymentIntentId] LIKE N'pi[_]fake[_]%' THEN 'fake' ELSE 'stripe:deployment' END,
                       o.[PaymentIntentId], o.[PlacedTotal], o.[Currency],
                       CASE
                           WHEN o.[Status] = 0 THEN 0
                           WHEN o.[Status] IN (1, 2, 3) THEN 1
                           WHEN EXISTS (SELECT 1 FROM [OrderStatusHistories] h WHERE h.[OrderId] = o.[Id] AND h.[Status] = 1) THEN 1
                           ELSE 3
                       END,
                       0, 0, o.[CreatedAt]
                FROM [Orders] o
                WHERE o.[PaymentIntentId] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // يحذف الدفعات والاستردادات وحسابات المتاجر (أسرارها المشفّرة) — للتطوير وحده؛ الطلبات ونيّاتها باقية.
            migrationBuilder.DropTable(
                name: "Refunds");

            migrationBuilder.DropTable(
                name: "StorePaymentAccounts");

            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
