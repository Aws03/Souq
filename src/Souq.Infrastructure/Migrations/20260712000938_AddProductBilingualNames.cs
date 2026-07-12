using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductBilingualNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // العمود القديم "Name" كان يحمل أسماءً عربية دوماً — التسمية الصحيحة له
            // هي NameAr، لا NameEn (خلاف ما يقترحه EF افتراضياً بالترتيب الأبجدي).
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Products",
                newName: "NameAr");

            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // نطابق قاعدة الكيان (NameEn يتردّد إلى NameAr إن غاب) على الصفوف
            // الموجودة فعلاً — بلا هذا، كل منتج قديم يظهر باسم إنجليزي فارغ
            // إلى أن تُترجمه الإدارة يدوياً.
            migrationBuilder.Sql("UPDATE [Products] SET [NameEn] = [NameAr] WHERE [NameEn] = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "NameAr",
                table: "Products",
                newName: "Name");
        }
    }
}
