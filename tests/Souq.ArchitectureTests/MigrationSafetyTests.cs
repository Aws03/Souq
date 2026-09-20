using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Souq.ArchitectureTests;

// ============================================================================
// سلامة الهجرات (M9). الهجرات تُطبَّق عند إقلاع التطبيق (R-18)، أي أن "النشر" و"تغيير المخطّط"
// حدث واحد — فما تتلفه هجرة يتلف وقت النشر بالضبط، لا في نافذة صيانة مخطَّطة.
//
// هذا الاختبار سقّاطة (ratchet) لا مانع: الهجرات القائمة مسجَّلة هنا بسببها، وأي هجرة *جديدة*
// تُسقط عموداً أو تعيد تسميته أو تضيّق نوعه تُفشل البناء حتى يسجّلها كاتبها — فيقرأ عندها
// ما يعنيه ذلك للرجوع وللنشر المتدرّج (BackupAndRestore + Migrations.md §7).
// لا نولّد Down مزيّفة لتغييرات لا رجعة فيها: الصدق هنا أنفع من الإيهام.
// ============================================================================
public class MigrationSafetyTests
{
    // عمليات تفقد بيانات موجودة أو تكسر تطبيقاً يقرأ المخطّط القديم.
    private static readonly Regex DestructiveOperation = new(
        @"migrationBuilder\.(DropColumn|DropTable|DropPrimaryKey|RenameColumn|RenameTable|AlterColumn)",
        RegexOptions.Compiled);

    // الهجرات التي تتلف بيانات، ولماذا. المفتاح اسم الهجرة بلا طابعها الزمني.
    private static readonly Dictionary<string, string> RecordedDestructive = new()
    {
        ["AddProductBilingualNames"] =
            "إعادة تسمية Products.Name إلى NameAr قبل إضافة NameEn.",
        ["Phase1AIntegrityPrecisionConcurrency"] =
            "تضييق أنواع المال إلى decimal(19,4) وجعل مفاتيح أجنبية إلزامية: قيمة بدقّة أعلى تُقرَّب، وصف يتيم يُرفض.",
        ["Phase3Identity"] =
            "اعتماد العملاء انتقل إلى Users ثم حُذفت أعمدته من Customers (PasswordHash، Role، رمزا إعادة التعيين).",
        ["Phase5Catalog"] =
            "نصوص المنتج وسعره وصورته وحالته انتقلت إلى الترجمات والمتغيّرات والصور ثم حُذفت أعمدتها الثمانية.",
        ["Phase6Inventory"] =
            "StockQuantity و LowStockThreshold انتقلا إلى InventoryItems ثم حُذفا من Products.",
        // مُسجَّلة لأن السقّاطة تلتقط AlterColumn كاملةً، لا لأنها تُفقد بيانات. **لا صفّ يتغيّر ولا
        // نوع يضيق**: التغيير الوحيد هو القيمة الافتراضية لعمود Tenants.EnabledModules (من كل
        // الوحدات إلى الفراغ)، وهي تخصّ الإدراج لا الصفوف القائمة. ما يلزم عند النشر هنا ليس نسخة
        // احتياطية بل الانتباه إلى أن الهجرة تنقل بيانات أيضاً (الخطة التأسيسية واشتراكات المتاجر
        // القائمة)، فهي ليست إضافيةً بالكامل.
        ["CommercialControlPlane"] =
            "تغيير القيمة الافتراضية لـ Tenants.EnabledModules إلى الفراغ (C1، ADR-0047): إغلاقُ افتراضٍ "
            + "كان يمنح كل الوحدات لصفٍّ يُدرَج بلا ذكرها. لا بيانات تُفقد ولا نوع يضيق.",
    };

    private static IEnumerable<(string Name, string Up, string Down)> Migrations()
    {
        foreach (var file in RepositoryPaths.Walk("src/Souq.Infrastructure/Migrations")
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal))
                     .Where(f => !f.Contains(".Designer.", StringComparison.Ordinal))
                     .Where(f => !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            var name = Regex.Replace(Path.GetFileNameWithoutExtension(file), @"^\d+_", "");
            var upAt = text.IndexOf("void Up(", StringComparison.Ordinal);
            var downAt = text.IndexOf("void Down(", StringComparison.Ordinal);
            if (upAt < 0 || downAt < 0 || downAt < upAt) continue;
            yield return (name, text[upAt..downAt], text[downAt..]);
        }
    }

    [Fact]
    public void كل_هجرة_متلِفة_للبيانات_مسجَّلة_بسببها()
    {
        var destructive = Migrations()
            .Where(m => DestructiveOperation.IsMatch(m.Up))
            .Select(m => m.Name)
            .ToList();

        destructive.Should().BeEquivalentTo(RecordedDestructive.Keys,
            "هجرة جديدة تُسقط عموداً أو تعيد تسميته أو تضيّق نوعه تحتاج تسجيلاً هنا بسببها، " +
            "لأنها تعني: نسخة احتياطية إلزامية قبل النشر، ولا نشر متدرّج بنسختين من التطبيق معاً");
    }

    [Fact]
    public void لكل_هجرة_دالة_رجوع_ولو_ناقصة()
    {
        // وجودها لا يعني أن الرجوع آمن (Migrations.md §7) — لكن غيابها يعني هجرة أحادية الاتجاه
        // بلا أن يقول أحد ذلك. الصراحة هي المطلوب هنا.
        Migrations().Where(m => m.Down.Length < 60).Select(m => m.Name)
            .Should().BeEmpty("هجرة بلا Down تُخفي أنها بلا رجعة");
    }

    [Fact]
    public void الهجرات_المتلِفة_وحدها_تحاول_استعادة_البيانات_في_الرجوع()
    {
        // كل Down يحاول إعادة ملء ما نقله Up إنما يفعل ذلك بـ SQL خام. وجودها في هجرة غير
        // مسجَّلة كمتلِفة يعني إمّا تصنيفاً خاطئاً أعلاه وإمّا Down يفعل أكثر مما يجب.
        var restoring = Migrations()
            .Where(m => m.Down.Contains("migrationBuilder.Sql(", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ToList();

        restoring.Should().OnlyContain(name => RecordedDestructive.ContainsKey(name) || IsDataMove(name));
    }

    // هجرات نقلت بيانات بلا إسقاط عمود (فالرجوع يحتاج SQL أيضاً) — مسجَّلة كي يبقى الفحص أعلاه ضيّقاً.
    private static bool IsDataMove(string name) =>
        name is "Phase2MultiTenancy" or "Phase10Coupons";
}
