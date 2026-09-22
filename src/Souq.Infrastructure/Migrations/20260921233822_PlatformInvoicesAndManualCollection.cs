using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlatformInvoicesAndManualCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // دورةُ الفوترة لخططٍ قائمة: **شهرٌ واحد لا صفر**. الصفرُ خارج المدى الذي يقبله
            // المجال (1..60)، فخطةٌ قائمة كانت ستُقرأ بقيمةٍ لا يستطيع أحدٌ كتابتها — والافتراضُ
            // المولَّد هو صفر، فيُصحَّح هنا بيد. ولا أثرَ تجاريّ: كلُّ خطةٍ قائمة بلا سعر أصلاً.
            migrationBuilder.AddColumn<int>(
                name: "BillingIntervalMonths",
                table: "Plans",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceAmount",
                table: "Plans",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceCurrency",
                table: "Plans",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingPeriods_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformBillingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    IssuerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IssuerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IssuerTaxNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    InvoiceNumberPrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreditNoteNumberPrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    GracePeriodDays = table.Column<int>(type: "int", nullable: false),
                    PaymentInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TaxProfileId = table.Column<int>(type: "int", nullable: true),
                    TaxCollectionEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBillingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformBillingSettings_TaxProfiles_TaxProfileId",
                        column: x => x.TaxProfileId,
                        principalTable: "TaxProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformDocumentSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Series = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformDocumentSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: true),
                    BillingPeriodId = table.Column<int>(type: "int", nullable: true),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IssuerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IssuerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IssuerTaxNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BilledToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BilledToTaxNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PaymentInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreditedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformInvoices_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformInvoices_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillableEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Meter = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BillingPeriodId = table.Column<int>(type: "int", nullable: false),
                    PlatformInvoiceId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillableEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillableEvents_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillableEvents_PlatformInvoices_PlatformInvoiceId",
                        column: x => x.PlatformInvoiceId,
                        principalTable: "PlatformInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillableEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PlatformInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNotes_PlatformInvoices_PlatformInvoiceId",
                        column: x => x.PlatformInvoiceId,
                        principalTable: "PlatformInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    TaxCategory = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformInvoiceLines_PlatformInvoices_PlatformInvoiceId",
                        column: x => x.PlatformInvoiceId,
                        principalTable: "PlatformInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformInvoicePayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformInvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformInvoicePayments_PlatformInvoices_PlatformInvoiceId",
                        column: x => x.PlatformInvoiceId,
                        principalTable: "PlatformInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlatformInvoicePayments_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreditNoteId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    TaxCategory = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillableEvents_BillingPeriodId_PlatformInvoiceId",
                table: "BillableEvents",
                columns: new[] { "BillingPeriodId", "PlatformInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillableEvents_PlatformInvoiceId",
                table: "BillableEvents",
                column: "PlatformInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillableEvents_TenantId_IdempotencyKey",
                table: "BillableEvents",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPeriods_TenantId_StartsAtUtc",
                table: "BillingPeriods",
                columns: new[] { "TenantId", "StartsAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPeriods_TenantId_Status",
                table: "BillingPeriods",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_CreditNoteId",
                table: "CreditNoteLines",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_Number",
                table: "CreditNotes",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_PlatformInvoiceId",
                table: "CreditNotes",
                column: "PlatformInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId",
                table: "CreditNotes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformBillingSettings_TaxProfileId",
                table: "PlatformBillingSettings",
                column: "TaxProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformDocumentSequences_Series",
                table: "PlatformDocumentSequences",
                column: "Series",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoiceLines_PlatformInvoiceId",
                table: "PlatformInvoiceLines",
                column: "PlatformInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoicePayments_PlatformInvoiceId",
                table: "PlatformInvoicePayments",
                column: "PlatformInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoicePayments_RecordedByUserId",
                table: "PlatformInvoicePayments",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoices_BillingPeriodId",
                table: "PlatformInvoices",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoices_Number",
                table: "PlatformInvoices",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoices_PlanId",
                table: "PlatformInvoices",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoices_Status_DueAtUtc",
                table: "PlatformInvoices",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformInvoices_TenantId_IssuedAtUtc",
                table: "PlatformInvoices",
                columns: new[] { "TenantId", "IssuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillableEvents");

            migrationBuilder.DropTable(
                name: "CreditNoteLines");

            migrationBuilder.DropTable(
                name: "PlatformBillingSettings");

            migrationBuilder.DropTable(
                name: "PlatformDocumentSequences");

            migrationBuilder.DropTable(
                name: "PlatformInvoiceLines");

            migrationBuilder.DropTable(
                name: "PlatformInvoicePayments");

            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropTable(
                name: "PlatformInvoices");

            migrationBuilder.DropTable(
                name: "BillingPeriods");

            migrationBuilder.DropColumn(
                name: "BillingIntervalMonths",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PriceAmount",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PriceCurrency",
                table: "Plans");
        }
    }
}
