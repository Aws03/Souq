using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceDunning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EscalatedAtUtc",
                table: "PlatformInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderAtUtc",
                table: "PlatformInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemindersSent",
                table: "PlatformInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "DunningEnabled",
                table: "PlatformBillingSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // ثلاثةٌ لا صفر: صفرٌ قيمةٌ مشروعة تعني «علّق بلا تذكير»، وهي ليست ما يقصده صفٌّ لم
            // يُضبَط بعد — فالافتراضُ يطابق ما تحمله الشيفرة لصفٍّ جديد.
            migrationBuilder.AddColumn<int>(
                name: "MaxRemindersBeforeSuspension",
                table: "PlatformBillingSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            // سبعةٌ لا صفر: الفاصلُ بين التذكيرات لا يقبله المجال صفراً (1..90)، فصفٌّ قائم كان
            // سيُقرأ بقيمةٍ لا يستطيع أحدٌ كتابتها. والقيمةُ بلا أثرٍ ما دامت المطالبة معطّلة،
            // وهي معطّلةٌ لكلّ صفٍّ قائم بحكم العمود أعلاه.
            migrationBuilder.AddColumn<int>(
                name: "ReminderIntervalDays",
                table: "PlatformBillingSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EscalatedAtUtc",
                table: "PlatformInvoices");

            migrationBuilder.DropColumn(
                name: "LastReminderAtUtc",
                table: "PlatformInvoices");

            migrationBuilder.DropColumn(
                name: "RemindersSent",
                table: "PlatformInvoices");

            migrationBuilder.DropColumn(
                name: "DunningEnabled",
                table: "PlatformBillingSettings");

            migrationBuilder.DropColumn(
                name: "MaxRemindersBeforeSuspension",
                table: "PlatformBillingSettings");

            migrationBuilder.DropColumn(
                name: "ReminderIntervalDays",
                table: "PlatformBillingSettings");
        }
    }
}
