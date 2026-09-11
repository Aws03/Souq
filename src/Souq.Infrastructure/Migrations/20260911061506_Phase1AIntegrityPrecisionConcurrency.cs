using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase1AIntegrityPrecisionConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── خطوات حفظ البيانات (مكتوبة يدوياً — DatabaseDesign.md §10) ──
            // (1) رموز إعادة التعيين القديمة نصّ صريح: لا نُبقي أسراراً مخزَّنة، ولن تطابق
            //     أي تجزئة بعد التغيير أصلاً — من طلب رابطاً قبل الترقية يطلب رابطاً جديداً.
            migrationBuilder.Sql(
                "UPDATE [Customers] SET [PasswordResetToken] = NULL, [PasswordResetTokenExpiry] = NULL " +
                "WHERE [PasswordResetToken] IS NOT NULL;");
            // (2) أسطر/سجلّات تاريخ يتيمة (OrderId NULL) لا يمكن الوصول إليها عبر أي طلب —
            //     تُحذف قبل جعل العمود إلزامياً وإلا فشل تحويله.
            migrationBuilder.Sql("DELETE FROM [OrderItems] WHERE [OrderId] IS NULL;");
            migrationBuilder.Sql("DELETE FROM [OrderStatusHistories] WHERE [OrderId] IS NULL;");
            // (3) فئات تشير لأب غير موجود (لم يكن هناك قيد): تصبح فئات جذرية قبل إضافة القيد.
            migrationBuilder.Sql(
                "UPDATE c SET c.[ParentId] = NULL FROM [Categories] c " +
                "WHERE c.[ParentId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [Categories] p WHERE p.[Id] = c.[ParentId]);");

            migrationBuilder.RenameColumn(
                name: "PasswordResetToken",
                table: "Customers",
                newName: "PasswordResetTokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_PasswordResetToken",
                table: "Customers",
                newName: "IX_Customers_PasswordResetTokenHash");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Products",
                type: "decimal(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Products",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "OrderStatusHistories",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "Orders",
                type: "decimal(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "OrderItems",
                type: "decimal(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Value",
                table: "Coupons",
                type: "decimal(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MinOrderAmount",
                table: "Coupons",
                type: "decimal(19,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Coupons",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Categories_ParentId",
                table: "Categories",
                column: "ParentId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Categories_ParentId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_ParentId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Coupons");

            migrationBuilder.RenameColumn(
                name: "PasswordResetTokenHash",
                table: "Customers",
                newName: "PasswordResetToken");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_PasswordResetTokenHash",
                table: "Customers",
                newName: "IX_Customers_PasswordResetToken");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Products",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)");

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "OrderStatusHistories",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "OrderItems",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)");

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "OrderItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "Value",
                table: "Coupons",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MinOrderAmount",
                table: "Coupons",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldNullable: true);
        }
    }
}
